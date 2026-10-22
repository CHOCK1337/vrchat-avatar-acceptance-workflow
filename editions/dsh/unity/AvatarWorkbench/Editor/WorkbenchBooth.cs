#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarWorkbench
{
    internal sealed class BoothProductImage
    {
        internal byte[] Data;
        internal string Source, Note;
        internal double ElapsedSeconds;
    }

    /// <summary>Public BOOTH metadata only. All Unity paths are captured before worker dispatch.</summary>
    [InitializeOnLoad]
    internal static class WorkbenchBooth
    {
        internal const string SourceRepository = "https://github.com/wuhutakeoffyoo/booth-cli";
        internal const string Attribution = "搜索组件：booth-cli 1.5.1 · Copyright © 2026 NyakoWW · MIT；Avatar Workbench 本地适配。";
        internal const string Notice = "本功能使用社区开源 booth-cli，与 BOOTH/pixiv、VRChat 及商品作者无官方合作或认证关系。商品名称、图片、说明与素材版权归原权利人。价格、授权条款和适配范围以卖家商品页为准，搜索匹配不代表已验证适配。商品链接与封面仅用于挑选参考；加入需求不代表已购买、获得授权或已经安装。请通过卖家正规渠道取得合法授权并遵守使用条款。本功能不提供登录、购买、付费文件下载或绕过访问限制。\n搜索会向 BOOTH 发送实际关键词和筛选条件，显示可见卡片时请求官方封面缩略图，点击详情时请求商品说明。点击大图时读取商品页实际提供的公开原图；下载超时或不可显示时回退压缩缩略图，并标明原因。默认本地词典只转换常见短词；主动选择独立 API 并搜索时，搜索文字会发送至你在搜索设置填写的服务地址，可能产生该服务的费用。此功能独立于 Codex，不改变全局模型设置。API 密钥仅用于当次服务认证，经本机进程输入传递，不进入商品缓存或请求记录；不会向搜索 API 发送工程路径、角色文件、截图或聊天历史，也不读取 BOOTH 登录凭据。翻页复用已生成关键词，不重复调用 API。当前仅搜索全年龄数字商品。公开页面或接口变化、网络失败及站点限流可能影响结果。";

        private const int MaxOutputChars = 1024 * 1024;
        private const int MaxThumbnailBytes = 2 * 1024 * 1024;
        private static readonly CancellationTokenSource Shutdown = new CancellationTokenSource();
        private static readonly SemaphoreSlim PythonGate = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim ProcessGate = new SemaphoreSlim(2, 2);
        // Shared by visible grid thumbnails and click-to-open product images.
        private static readonly SemaphoreSlim ThumbnailGate = new SemaphoreSlim(2, 2);
        private static readonly object ProcessLock = new object();
        private static readonly HashSet<Process> ActiveProcesses = new HashSet<Process>();
        private static readonly HttpClient Images = CreateImageClient();
        private static string cachedPython;

        static WorkbenchBooth()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        internal static Task<JObject> SearchAsync(string query, string category, int maxPrice, string sort, int page, CancellationToken token,
            JObject translation = null, JObject queryPlan = null)
        {
            var paths = CapturePaths();
            var request = new JObject { ["action"] = "search", ["query"] = query ?? "", ["category"] = category ?? "",
                ["max_price"] = maxPrice, ["sort"] = sort ?? "new", ["page"] = page };
            if (translation != null)
            {
                var configuration = new JObject();
                foreach (var key in new[] { "mode", "base_url", "api_key", "model" })
                    if (translation[key] != null) configuration[key] = translation[key].DeepClone();
                request["translation"] = configuration;
            }
            if (queryPlan != null)
            {
                var plan = new JObject();
                foreach (var key in new[] { "version", "original_query", "keywords", "source", "translated", "note" })
                    if (queryPlan[key] != null) plan[key] = queryPlan[key].DeepClone();
                request["query_plan"] = plan;
            }
            return InvokeAsync(request, paths, token);
        }

        internal static Task<JObject> DetailAsync(string itemId, CancellationToken token)
        {
            var paths = CapturePaths();
            return InvokeAsync(new JObject { ["action"] = "detail", ["item_id"] = itemId ?? "" }, paths, token);
        }

        internal static Task<byte[]> ThumbnailAsync(string url, CancellationToken token)
        {
            var paths = CapturePaths();
            return ThumbnailCoreAsync(ThumbnailUri(url), paths.CacheRoot, token);
        }

        internal static Task<BoothProductImage> ProductImageAsync(string itemId, string thumbnailUrl, CancellationToken token)
        {
            var paths = CapturePaths();
            return ProductImageCoreAsync(itemId, thumbnailUrl, paths, token);
        }

        private static async Task<BoothProductImage> ProductImageCoreAsync(string itemId, string thumbnailUrl, Paths paths, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Shutdown.Token))
            {
                var ct = linked.Token;
                // Queue time does not consume the Python helper's download budget.
                await ThumbnailGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var result = await InvokeAsync(new JObject { ["action"] = "image", ["item_id"] = itemId ?? "",
                        ["thumbnail_url"] = thumbnailUrl ?? "" }, paths, ct).ConfigureAwait(false);
                    return await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        var file = (string)result["path"];
                        var root = Path.GetFullPath(paths.CacheRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                        if (string.IsNullOrEmpty(file) || !Path.GetFullPath(file).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("商品图片缓存路径无效，已停止读取。");
                        var info = new FileInfo(file);
                        if (!info.Exists || info.Length <= 0 || info.Length > 8L * 1024 * 1024)
                            throw new IOException("商品图片缓存缺失或超出大小限制。");
                        var source = (string)result["source"];
                        if (source != "original" && source != "thumbnail") throw new IOException("商品图片来源标记无效。");
                        var bytes = File.ReadAllBytes(file);
                        ct.ThrowIfCancellationRequested();
                        return new BoothProductImage { Data = bytes, Source = source, Note = (string)result["note"] ?? "",
                            ElapsedSeconds = (double?)result["elapsed_seconds"] ?? 0 };
                    }, ct).ConfigureAwait(false);
                }
                finally { ThumbnailGate.Release(); }
            }
        }

        private sealed class Paths
        {
            internal string Script, CacheRoot;
        }

        private static Paths CapturePaths()
        {
            // Call from the editor thread; background work does not touch Unity APIs.
            var assets = Application.dataPath;
            return new Paths
            {
                Script = Path.Combine(assets, "AvatarWorkbench", "Editor", "BoothCli~", "workbench_bridge.py"),
                CacheRoot = Path.Combine(Path.GetDirectoryName(assets) ?? assets, "Library", "AvatarWorkbench", "Booth")
            };
        }

        private static async Task<JObject> InvokeAsync(JObject request, Paths paths, CancellationToken token)
        {
            var secret = ((string)request["translation"]?["api_key"] ?? "").Trim();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Shutdown.Token))
            {
                linked.CancelAfter(TimeSpan.FromSeconds(80));
                var ct = linked.Token;
                try
                {
                    return await Task.Run(async () =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!File.Exists(paths.Script)) throw new InvalidOperationException("BOOTH 搜索组件缺失，请重新安装完整工作台插件。");
                        Directory.CreateDirectory(paths.CacheRoot);
                        var python = await ResolvePythonAsync(paths.CacheRoot, ct).ConfigureAwait(false);
                        request["cache_root"] = paths.CacheRoot;
                        await ProcessGate.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            var result = await RunProcessAsync(python, "-I -B " + QuoteArgument(paths.Script),
                                request.ToString(Formatting.None), paths.CacheRoot, MaxOutputChars, ct).ConfigureAwait(false);
                            ct.ThrowIfCancellationRequested();
                            if (result.ExitCode != 0)
                                throw new InvalidOperationException("BOOTH 搜索组件未能完成请求。" + SafeError(Redact(result.Error, secret)));
                            JObject envelope;
                            try { envelope = JObject.Parse(result.Output); }
                            catch (JsonException) { throw new InvalidOperationException("BOOTH 搜索组件返回的内容无法读取，请稍后重试。"); }
                            if ((bool?)envelope["ok"] != true)
                                throw new InvalidOperationException(Redact((string)envelope["error"] ?? "BOOTH 请求失败，请稍后重试。", secret));
                            var data = envelope["data"] as JObject;
                            if (data == null) throw new InvalidOperationException("BOOTH 搜索没有返回有效商品数据。");
                            RedactStrings(data, secret);
                            return data;
                        }
                        finally { ProcessGate.Release(); }
                    }, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (token.IsCancellationRequested || Shutdown.IsCancellationRequested) throw;
                    throw new TimeoutException("BOOTH 请求已超时，已停止本次搜索；请稍后重试或打开商品网站。");
                }
                catch (Exception error)
                {
                    var message = Redact(error.Message, secret);
                    if (!string.IsNullOrEmpty(secret) || message != error.Message) throw new InvalidOperationException(message);
                    throw;
                }
                finally
                {
                    // The caller keeps its own session configuration; this copy
                    // is transient and is never placed in feedback or state files.
                    request.Remove("translation");
                    secret = "";
                }
            }
        }

        private static async Task<string> ResolvePythonAsync(string cacheRoot, CancellationToken ct)
        {
            await PythonGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrEmpty(cachedPython) && File.Exists(cachedPython)) return cachedPython;
                foreach (var candidate in PythonCandidates().Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!File.Exists(candidate)) continue;
                    using (var probe = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        probe.CancelAfter(TimeSpan.FromSeconds(3));
                        try
                        {
                            var result = await RunProcessAsync(candidate,
                                "-I -B -c \"import sys;print('AWPY' if sys.version_info >= (3,10) else 'OLD')\"",
                                "", cacheRoot, 256, probe.Token).ConfigureAwait(false);
                            if (result.ExitCode == 0 && result.Output.Trim() == "AWPY") return cachedPython = candidate;
                        }
                        catch (OperationCanceledException) { ct.ThrowIfCancellationRequested(); }
                        catch (System.ComponentModel.Win32Exception) { }
                        catch (IOException) { }
                    }
                }
                throw new InvalidOperationException("未找到可用的 Python 3.10 或更新版本。工作台已检查现有 Codex 运行时和 PATH，不会自动安装；仍可在 BOOTH 网站查看商品。");
            }
            finally { PythonGate.Release(); }
        }

        private static IEnumerable<string> PythonCandidates()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(profile, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "python", "python.exe");
            yield return Path.Combine(profile, ".cache", "codex-runtimes", "codex-primary-runtime", "dependencies", "python", "bin", "python3");
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var path = dir.Trim().Trim('"');
                if (string.IsNullOrEmpty(path) || path.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                yield return Path.Combine(path, "python.exe");
                yield return Path.Combine(path, "python3.exe");
                if (Path.DirectorySeparatorChar != '\\') yield return Path.Combine(path, "python3");
            }
        }

        private sealed class ProcessResult
        {
            internal string Output, Error;
            internal int ExitCode;
        }

        private static async Task<ProcessResult> RunProcessAsync(string executable, string arguments, string input,
            string cacheRoot, int outputLimit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var temp = Path.Combine(cacheRoot, "tmp");
            Directory.CreateDirectory(temp);
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = cacheRoot
            };
            // Do not inherit provider keys, Python hooks, proxy credentials, or user .env configuration.
            info.EnvironmentVariables.Clear();
            foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "SystemDrive", "USERPROFILE", "HOME" })
            {
                var value = Environment.GetEnvironmentVariable(key);
                if (!string.IsNullOrEmpty(value)) info.EnvironmentVariables[key] = value;
            }
            info.EnvironmentVariables["TEMP"] = temp;
            info.EnvironmentVariables["TMP"] = temp;
            using (var process = new Process { StartInfo = info, EnableRaisingEvents = true })
            {
                var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (sender, args) => exited.TrySetResult(true);
                if (!process.Start()) throw new IOException("无法启动现有 Python 运行时。");
                lock (ProcessLock) ActiveProcesses.Add(process);
                using (ct.Register(() => TryKill(process)))
                {
                    try
                    {
                        var stdout = ReadBoundedAsync(process.StandardOutput, outputLimit, process);
                        var stderr = ReadBoundedAsync(process.StandardError, 8192, process);
                        var bytes = new UTF8Encoding(false).GetBytes(input);
                        await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
                        process.StandardInput.Close();
                        if (process.HasExited) exited.TrySetResult(true);
                        await exited.Task.ConfigureAwait(false);
                        var values = await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                        ct.ThrowIfCancellationRequested();
                        return new ProcessResult { Output = values[0], Error = values[1], ExitCode = process.ExitCode };
                    }
                    finally
                    {
                        TryKill(process);
                        lock (ProcessLock) ActiveProcesses.Remove(process);
                    }
                }
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, Process process)
        {
            var result = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (count == 0) break;
                if (result.Length + count > maximum)
                {
                    TryKill(process);
                    throw new IOException("BOOTH 响应过大，已停止本次请求。");
                }
                result.Append(buffer, 0, count);
            }
            return result.ToString();
        }

        private static string QuoteArgument(string value)
        {
            // This is a fixed local script path, never a search term or shell command.
            if (value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                throw new InvalidOperationException("BOOTH 搜索组件路径包含不支持的字符。");
            return "\"" + value + "\"";
        }

        private static string SafeError(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            return "\n" + value.Trim().Substring(0, Math.Min(value.Trim().Length, 800));
        }

        private static string Redact(string value, string secret)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(secret)) return value ?? "";
            return value.Replace(secret, "[已隐藏密钥]").Replace(Uri.EscapeDataString(secret), "[已隐藏密钥]");
        }

        private static void RedactStrings(JToken token, string secret)
        {
            if (string.IsNullOrEmpty(secret)) return;
            var value = token as JValue;
            if (value != null && value.Type == JTokenType.String) value.Value = Redact((string)value.Value, secret);
            else foreach (var child in token.Children()) RedactStrings(child, secret);
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static HttpClient CreateImageClient()
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false };
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AvatarWorkbench/0.1.7");
            client.DefaultRequestHeaders.Referrer = new Uri("https://booth.pm/");
            return client;
        }

        private static Uri ThumbnailUri(string value)
        {
            Uri source;
            if (!Uri.TryCreate(value, UriKind.Absolute, out source) || source.Scheme != "https" ||
                !source.Host.Equals("booth.pximg.net", StringComparison.OrdinalIgnoreCase) || !source.IsDefaultPort || !string.IsNullOrEmpty(source.UserInfo))
                throw new InvalidOperationException("封面地址不是 BOOTH 官方缩略图地址。");
            var path = Regex.Replace(source.AbsolutePath, "^/c/[^/]+/", "/");
            // Recover URLs emitted by the first local adapter before the real
            // BOOTH thumbnail preset was verified. New responses preserve the
            // actual page filename, including _base_resized, unchanged.
            if (source.AbsolutePath.StartsWith("/c/320x320_a2/", StringComparison.Ordinal) &&
                path.IndexOf("_base_resized.", StringComparison.Ordinal) < 0)
                path = Regex.Replace(path, "(\\.[A-Za-z0-9]+)$", "_base_resized$1");
            return new Uri("https://booth.pximg.net/c/300x300_a2_g5" + path);
        }

        private static async Task<byte[]> ThumbnailCoreAsync(Uri uri, string cacheRoot, CancellationToken token)
        {
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, Shutdown.Token))
            {
                linked.CancelAfter(TimeSpan.FromSeconds(30));
                var ct = linked.Token;
                await ThumbnailGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await Task.Run(async () =>
                    {
                        var directory = Path.Combine(cacheRoot, "thumbnails");
                        Directory.CreateDirectory(directory);
                        string key;
                        using (var hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).Replace("-", "").ToLowerInvariant();
                        var file = Path.Combine(directory, key + ".thumb");
                        if (File.Exists(file))
                        {
                            var info = new FileInfo(file);
                            if (info.Length > 0 && info.Length <= MaxThumbnailBytes && info.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-7))
                            {
                                ct.ThrowIfCancellationRequested();
                                return File.ReadAllBytes(file);
                            }
                        }
                        using (var response = await Images.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                        {
                            if (response.StatusCode != HttpStatusCode.OK)
                                throw new IOException("商品封面暂时不可用（HTTP " + (int)response.StatusCode + "）。");
                            if (response.Content.Headers.ContentLength > MaxThumbnailBytes) throw new IOException("商品封面过大，已跳过。");
                            var media = response.Content.Headers.ContentType == null ? "" : response.Content.Headers.ContentType.MediaType;
                            if (media != "image/jpeg" && media != "image/png" && media != "image/webp") throw new IOException("商品封面格式不可用。");
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var memory = new MemoryStream())
                            {
                                var buffer = new byte[8192];
                                while (true)
                                {
                                    var count = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false);
                                    if (count == 0) break;
                                    if (memory.Length + count > MaxThumbnailBytes) throw new IOException("商品封面过大，已跳过。");
                                    memory.Write(buffer, 0, count);
                                }
                                var bytes = memory.ToArray();
                                ct.ThrowIfCancellationRequested();
                                // Cache only the resized endpoint, capped to 64 MiB/256 files.
                                try
                                {
                                    File.WriteAllBytes(file, bytes);
                                    var files = new DirectoryInfo(directory).GetFiles("*.thumb").OrderBy(x => x.LastWriteTimeUtc).ToList();
                                    var total = files.Sum(x => x.Length);
                                    var remainingFiles = files.Count;
                                    foreach (var old in files)
                                    {
                                        if (remainingFiles <= 256 && total <= 64L * 1024 * 1024) break;
                                        total -= old.Length;
                                        try { old.Delete(); } catch (IOException) { }
                                        remainingFiles--;
                                    }
                                }
                                catch (IOException) { }
                                catch (UnauthorizedAccessException) { }
                                return bytes;
                            }
                        }
                    }, ct).ConfigureAwait(false);
                }
                finally { ThumbnailGate.Release(); }
            }
        }

        private static void Stop()
        {
            if (Shutdown.IsCancellationRequested) return;
            Shutdown.Cancel();
            lock (ProcessLock) foreach (var process in ActiveProcesses) TryKill(process);
            Images.Dispose();
        }
    }
}
#endif

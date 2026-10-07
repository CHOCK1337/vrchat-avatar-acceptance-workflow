using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    // Anonymous share reading only. Browser/account cookies and download credentials are never read.
    internal sealed class WorkbenchBaiduShare : IDisposable
    {
        readonly CookieContainer cookies = new CookieContainer();
        readonly HttpClient client;
        string shareId, shareOwner;
        internal string Url { get; private set; }
        internal bool CanReadFolders => !string.IsNullOrEmpty(shareId) && !string.IsNullOrEmpty(shareOwner);
        internal WorkbenchBaiduShare()
        {
            client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = cookies, UseCookies = true }) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 AvatarWorkbench/0.2.2");
        }
        internal static JObject Parse(string text, string explicitCode = "")
        {
            text = (text ?? "").Normalize(NormalizationForm.FormKC);
            var match = Regex.Match(text, @"https?://(?:pan|yun)\.baidu\.com/(?:s/[A-Za-z0-9_-]+(?:\?[^\s<>\""，。]+)?|(?:share|wap)/init\?[^\s<>\""，。]+)", RegexOptions.IgnoreCase);
            if (!match.Success) throw new IOException("请粘贴百度网盘的分享链接，支持 /s/ 和 /share/init 链接。");
            var uri = new Uri(match.Value); var query = Query(uri.Query);
            string shortId = uri.AbsolutePath.StartsWith("/s/", StringComparison.OrdinalIgnoreCase) ? uri.AbsolutePath.Substring(3) : query.TryGetValue("surl", out var surl) ? "1" + surl : "";
            if (!Regex.IsMatch(shortId, @"^1[A-Za-z0-9_-]{3,159}$")) throw new IOException("分享编号无效，请重新复制完整链接。");
            string code = (explicitCode ?? "").Trim();
            if (string.IsNullOrEmpty(code) && query.TryGetValue("pwd", out var urlCode)) code = urlCode;
            if (string.IsNullOrEmpty(code)) { var password = Regex.Match(text, @"(?:提取码|访问码|密码)\s*[:：]?\s*([A-Za-z0-9]{4})(?![A-Za-z0-9])"); if (password.Success) code = password.Groups[1].Value; }
            if (!string.IsNullOrEmpty(code) && !Regex.IsMatch(code, @"^[A-Za-z0-9]{4}$")) throw new IOException("提取码应为 4 位字母或数字。");
            return new JObject { ["id"] = "baidu-" + shortId, ["url"] = "https://pan.baidu.com/s/" + shortId, ["code"] = code, ["short_id"] = shortId };
        }
        static Dictionary<string, string> Query(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in query.TrimStart('?').Split('&')) { int at = part.IndexOf('='); if (at > 0) result[Uri.UnescapeDataString(part.Substring(0, at))] = Uri.UnescapeDataString(part.Substring(at + 1)); }
            return result;
        }
        internal async Task<JObject> ReadRoot(JObject share, CancellationToken token)
        {
            Url = (string)share["url"];
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(35)); token = deadline.Token;
                string page = await Request(Url, null, token); CheckExpired(page);
                string code = (string)share["code"] ?? "";
                if (!string.IsNullOrEmpty(code))
                {
                    string shortId = ((string)share["short_id"]).Substring(1);
                    var verify = JObject.Parse(await Request("https://pan.baidu.com/share/verify?surl=" + Uri.EscapeDataString(shortId) + "&channel=chunlei&web=1&app_id=250528&clienttype=0", new Dictionary<string, string> { ["pwd"] = code }, token));
                    CheckError(verify); string access = (string)verify["randsk"];
                    if (!string.IsNullOrEmpty(access)) cookies.Add(new Uri("https://pan.baidu.com/"), new Cookie("BDCLND", access, "/"));
                    page = await Request(Url, null, token); CheckExpired(page);
                }
                var data = ParsePage(page);
                if (data == null) throw new IOException(string.IsNullOrEmpty(code) && page.Contains("提取码") ? "分享需要提取码，请补充后再读取。" : "百度网盘没有返回可读文件列表，可能需要登录或访问验证。可打开分享，在客户端下载后关联本地目录。");
                shareId = WorkbenchData.Text(data["shareid"]); shareOwner = WorkbenchData.Text(data["share_uk"] ?? data["uk"]);
                var files = data["file_list"] as JArray ?? data["list"] as JArray;
                if (files == null) throw new IOException("分享页面格式发生变化，没有得到文件列表；没有记录为已读取。");
                return Listing(files, "", 1, files.Count >= 100);
            }
        }
        internal async Task<JObject> ReadFolder(string path, int page, CancellationToken token)
        {
            if (string.IsNullOrEmpty(shareId) || string.IsNullOrEmpty(shareOwner)) throw new IOException("请先重新读取分享，建立本次临时访问会话。");
            string request = "https://pan.baidu.com/share/list?uk=" + Uri.EscapeDataString(shareOwner) + "&shareid=" + Uri.EscapeDataString(shareId)
                + "&order=name&desc=0&showempty=0&web=1&num=100&page=" + Math.Max(1, page) + "&app_id=250528&clienttype=0&root=" + (string.IsNullOrEmpty(path) ? "1" : "0&dir=" + Uri.EscapeDataString(path));
            var data = JObject.Parse(await Request(request, null, token)); CheckError(data);
            var files = data["list"] as JArray; if (files == null) throw new IOException("百度网盘没有返回子目录列表。");
            return Listing(files, path, page, files.Count >= 100);
        }
        static JObject Listing(JArray files, string path, int page, bool more)
        {
            return new JObject { ["read_at"] = WorkbenchData.Now, ["path"] = path, ["page"] = page, ["has_next"] = more,
                ["files"] = new JArray(files.OfType<JObject>().Take(300).Select(f => new JObject { ["name"] = WorkbenchData.Text(f["server_filename"] ?? f["filename"], "未命名文件"),
                    ["remote_path"] = WorkbenchData.Text(f["path"]), ["directory"] = (int?)f["isdir"] == 1, ["bytes"] = (long?)f["size"] ?? 0 })) };
        }
        internal static JObject ParsePage(string html)
        {
            var script = Regex.Match(html, @"<script[^>]*\bid\s*=\s*['\"" ]*locals-data['\"" ][^>]*>([\s\S]*?)</script>", RegexOptions.IgnoreCase);
            if (script.Success) { try { return JObject.Parse(WebUtility.HtmlDecode(script.Groups[1].Value)); } catch (Newtonsoft.Json.JsonException) { } }
            foreach (string marker in new[] { "yunData", "locals.mset", "locals.set" })
            {
                int at = html.IndexOf(marker, StringComparison.Ordinal); if (at < 0) continue;
                int start = html.IndexOf('{', at); if (start < 0) continue; int depth = 0; bool quoted = false, escaped = false;
                for (int i = start; i < html.Length; i++)
                {
                    char c = html[i]; if (quoted) { if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false; continue; }
                    if (c == '"') quoted = true; else if (c == '{') depth++; else if (c == '}' && --depth == 0) { try { return JObject.Parse(html.Substring(start, i - start + 1)); } catch (Newtonsoft.Json.JsonException) { break; } }
                }
            }
            return null;
        }
        static void CheckExpired(string page)
        {
            if (new[] { "分享的文件已经被删除", "分享的文件已经被取消", "分享已过期", "链接已过期", "分享链接已失效" }.Any(page.Contains)) throw new IOException("分享已失效或文件被移除，请更换链接。");
        }
        static void CheckError(JObject data)
        {
            int error = (int?)data["errno"] ?? -999; if (error == 0) return;
            if (error == -9) throw new IOException("提取码未通过，请核对后再读取。");
            if (error == -12 || error == -10) throw new IOException("分享已失效或无权访问。");
            if (error == -62 || error == -63 || data["vcode"] != null) throw new IOException("百度网盘要求验证码或访问验证。请在浏览器中处理；工作台没有继续尝试。");
            throw new IOException("百度网盘拒绝本次读取（错误 " + error + "），可能需要登录或访问验证。可在客户端下载后关联本地目录。");
        }
        async Task<string> Request(string url, Dictionary<string, string> form, CancellationToken token)
        {
            for (int redirect = 0; redirect <= 3; redirect++)
            {
                var uri = new Uri(url); if (uri.Scheme != "https" || !new[] { "pan.baidu.com", "yun.baidu.com" }.Contains(uri.Host.ToLowerInvariant()) || !string.IsNullOrEmpty(uri.UserInfo)) throw new IOException("分享跳转到了登录或验证页面，请在浏览器中处理。");
                using (var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, uri))
                {
                    request.Headers.Referrer = new Uri(Url ?? "https://pan.baidu.com/");
                    if (form != null) request.Content = new FormUrlEncodedContent(form);
                    using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token))
                    {
                        int status = (int)response.StatusCode;
                        if (status >= 300 && status < 400 && response.Headers.Location != null) { url = new Uri(uri, response.Headers.Location).AbsoluteUri; form = null; continue; }
                        if (!response.IsSuccessStatusCode) throw new IOException("百度网盘读取失败（HTTP " + status + "），请稍后手动重试。");
                        if (response.Content.Headers.ContentLength > 2 * 1024 * 1024) throw new IOException("分享页面太大，本次读取已停止。");
                        using (var stream = await response.Content.ReadAsStreamAsync()) using (var output = new MemoryStream())
                        {
                            var buffer = new byte[16384]; int count;
                            while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0) { if (output.Length + count > 2 * 1024 * 1024) throw new IOException("分享页面太大，本次读取已停止。"); output.Write(buffer, 0, count); }
                            return Encoding.UTF8.GetString(output.ToArray());
                        }
                    }
                }
            }
            throw new IOException("分享跳转次数过多，请在浏览器中检查链接。");
        }
        public void Dispose() => client.Dispose();
    }
}

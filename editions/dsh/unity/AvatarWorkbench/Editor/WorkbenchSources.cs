using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    // This index belongs to the GUI; neither scanning nor saving imports project assets.
    internal static class WorkbenchSources
    {
        internal static string FilePath => Path.Combine(Path.GetDirectoryName(WorkbenchData.WindowFile), "sources.json");
        internal static JObject Load()
        {
            var value = File.Exists(FilePath) ? WorkbenchCatalogMetadata.ReadJson(FilePath, 8 * 1024 * 1024) : new JObject();
            foreach (string key in new[] { "directories", "shares", "references" })
            {
                if (value[key] != null && !(value[key] is JArray)) throw new IOException("来源索引格式有误，原记录已保留：" + key);
                if (value[key] == null) value[key] = new JArray();
            }
            value["format"] = "avatar-workbench-sources-v1"; return value;
        }
        internal static void Save(JObject value)
        {
            if (Encoding.UTF8.GetByteCount(value.ToString()) > 7500 * 1024) throw new IOException("目录索引太大，请移除不用的目录记录或缩小读取范围；素材文件不会删除。");
            WorkbenchData.Write(FilePath, value);
        }
        internal static string DirectoryPath(string value)
        {
            string path = Path.GetFullPath((value ?? "").Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(path)) throw new IOException("目录不存在，请选择已下载的素材文件夹。");
            if (path == Path.GetPathRoot(path).TrimEnd('\\', '/')) throw new IOException("请选择素材文件夹，不能读取整个磁盘。");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("请选择实际素材目录；不跟随链接或目录联接。");
            return path;
        }
        internal static string AssetPath(string full, string project)
        {
            string normalized = Path.GetFullPath(full).Replace('\\', '/'), root = Path.GetFullPath(project).TrimEnd('\\', '/').Replace('\\', '/') + "/";
            if (normalized.StartsWith(root + "Assets/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(root + "Packages/", StringComparison.OrdinalIgnoreCase)) return normalized.Substring(root.Length);
            return normalized;
        }
        internal sealed class Progress
        {
            internal int Directories, Files, Found;
            internal string Label => "读取了 " + Volatile.Read(ref Directories) + " 个目录、" + Volatile.Read(ref Files) + " 个文件 · 找到 " + Volatile.Read(ref Found) + " 项素材";
        }
        internal static JObject Scan(string scope, string project, bool recursive, CancellationToken cancellation, Progress progress)
        {
            scope = DirectoryPath(scope);
            var results = new JArray(); var pending = new Queue<Tuple<string, int>>(); pending.Enqueue(Tuple.Create(scope, 0));
            int skipped = 0; bool limited = false; var scopeFiles = new List<string>();
            var exclude = new HashSet<string>(new[] { "Library", "Temp", "Logs", "obj", ".git", "node_modules", ".avatar-workbench-update" }, StringComparer.OrdinalIgnoreCase);
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested(); var next = pending.Dequeue();
                if (progress.Directories >= 512 || progress.Files >= 20000 || results.Count >= 300) { limited = true; break; }
                Interlocked.Increment(ref progress.Directories);
                var files = new List<string>();
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(next.Item1))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (progress.Files >= 20000 || files.Count >= 4000) { limited = true; break; }
                        var attributes = File.GetAttributes(path); if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (recursive && next.Item2 < 8 && !exclude.Contains(Path.GetFileName(path))) pending.Enqueue(Tuple.Create(path, next.Item2 + 1));
                            else if (recursive && next.Item2 >= 8) limited = true;
                            continue;
                        }
                        files.Add(path); Interlocked.Increment(ref progress.Files);
                    }
                }
                catch (IOException) { skipped++; } catch (UnauthorizedAccessException) { skipped++; }
                scopeFiles.AddRange(files);
                bool plugin = false;
                foreach (string path in files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    cancellation.ThrowIfCancellationRequested(); string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (new[] { ".cs", ".asmdef" }.Contains(extension) || Path.GetFileName(path) == "package.json") plugin = true;
                    string kind = extension == ".prefab" ? "prefab" : extension == ".unitypackage" ? "package" : new[] { ".zip", ".7z", ".rar", ".tar", ".gz" }.Contains(extension) ? "archive" : "";
                    if (string.IsNullOrEmpty(kind)) continue;
                    if (results.Count >= 300) { limited = true; break; }
                    long bytes = 0; try { bytes = new FileInfo(path).Length; } catch (IOException) { }
                    results.Add(Entry(path, project, kind, null, bytes)); Interlocked.Increment(ref progress.Found);
                }
                if (plugin && results.Count < 300)
                {
                    // Script directories are one resource, rather than hundreds of independent scripts.
                    results.Add(Entry(next.Item1, project, "plugin", null, 0)); Interlocked.Increment(ref progress.Found);
                }
            }
            WorkbenchCatalogMetadata.Enrich(results, scopeFiles, scope, cancellation);
            return new JObject { ["items"] = results, ["directories_read"] = progress.Directories, ["files_read"] = progress.Files,
                ["skipped"] = skipped, ["limited"] = limited, ["read_at"] = WorkbenchData.Now };
        }
        static JObject Entry(string path, string project, string kind, string cover, long bytes) => new JObject {
            ["id"] = "catalog-" + WorkbenchData.Hash(Encoding.UTF8.GetBytes(path.ToLowerInvariant())).Substring(0, 16),
            ["name"] = Path.GetFileNameWithoutExtension(path), ["path"] = AssetPath(path, project), ["full_path"] = path,
            ["kind"] = kind, ["thumbnail"] = cover ?? "", ["bytes"] = bytes, ["state"] = "local_only"
        };
    }

    public static class WorkbenchSourceApi
    {
        // Read on demand from the connected Editor. No polling or task state writes.
        public static string ReadCatalog()
        {
            var data = WorkbenchSources.Load();
            foreach (string field in new[] { "code_draft", "share_draft", "name_draft", "directory_draft" }) data.Remove(field);
            foreach (var share in ((JArray)data["shares"]).OfType<JObject>()) share.Remove("code");
            return data.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AvatarWorkbench
{
    // File facts and cached product facts remain separate. A filename is never proof of compatibility.
    internal static class WorkbenchCatalogMetadata
    {
        static readonly Regex BoothLink = new Regex(@"https?://(?:[\w-]+\.)?booth\.pm/(?:[a-z]{2}/)?items/([0-9]{5,12})(?:\b|/)", RegexOptions.IgnoreCase);
        static readonly Regex Structural = new Regex(@"^(prefabs?|resources|assets|materials?|textures?|scripts?|editor|fbx|models?|animations?|milfy|manuka|shinano|rurune|eku|kikyo|selestia|milltina|萌|マヌカ|しなの)$", RegexOptions.IgnoreCase);
        static readonly Regex TexturePath = new Regex(@"(?:^|[/\\])(textures?|materials?|masks?|uvmaps?|icons?)(?:[/\\]|$)", RegexOptions.IgnoreCase);
        static readonly Regex TextureName = new Regex(@"(?:^|[_\s.-])(uv|mask|normal|nm|ao|roughness|metallic|opacity|emission|matcap|color|albedo)(?:$|[_\s.-])|色卡|贴图|ノーマル", RegexOptions.IgnoreCase);
        internal static JObject ReadJson(string path, long maxBytes)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maxBytes) throw new IOException("素材索引不存在或超过读取上限。");
            using (var text = new StreamReader(path, Encoding.UTF8, true))
            using (var json = new JsonTextReader(text) { MaxDepth = 48, DateParseHandling = DateParseHandling.None }) return JObject.Load(json);
        }
        internal static string Clip(JToken value, int max = 160) { string s = WorkbenchData.Text(value); return s.Length <= max ? s : s.Substring(0, max); }
        internal static bool IsPicture(string path) => new[] { ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(path ?? "").ToLowerInvariant());
        internal static bool IsCover(string path) => IsPicture(path) && !TexturePath.IsMatch(path ?? "") && !TextureName.IsMatch(Path.GetFileNameWithoutExtension(path ?? ""));
        static bool Under(string path, string root) => path.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static string NameKey(string name) => Regex.Replace((name ?? "").ToLowerInvariant(), @"[\s_.-]+", "");
        static string ProductRoot(string path, string scope)
        {
            string root = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            while (!string.Equals(root, scope, StringComparison.OrdinalIgnoreCase) && Structural.IsMatch(Path.GetFileName(root))) root = Path.GetDirectoryName(root);
            for (string ancestor = root; ancestor != null && (Under(ancestor, scope) || string.Equals(ancestor, scope, StringComparison.OrdinalIgnoreCase)); ancestor = Path.GetDirectoryName(ancestor)) {
                var id = Regex.Match(Path.GetFileName(ancestor), @"^[0-9]{5,9}(?=\D|$)"); DateTime date;
                if (id.Success && !DateTime.TryParseExact(id.Value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)) return ancestor;
            }
            return Under(root, scope) || string.Equals(root, scope, StringComparison.OrdinalIgnoreCase) ? root : scope;
        }
        static int PictureScore(string picture, string root, string stem)
        {
            if (!IsCover(picture)) return -1;
            string name = Path.GetFileNameWithoutExtension(picture), lower = name.ToLowerInvariant();
            string key = NameKey(name), stemKey = NameKey(stem);
            int score = key == stemKey ? 200 : stemKey.Length >= 4 && key.StartsWith(stemKey) ? 150 : 0;
            if (Regex.IsMatch(lower, @"cover|thumbnail|preview|sample|商品|封面|预览|サムネ|メイン")) score += 90;
            if (string.Equals(name, Path.GetFileName(root), StringComparison.OrdinalIgnoreCase)) score += 100;
            if (Regex.IsMatch(lower, @"闲鱼|网盘|广告|二维码|qrcode|logo")) return -1;
            // Ordinary pictures only belong to the product's root, not arbitrary texture subfolders.
            if (score == 0 && !string.Equals(Path.GetDirectoryName(picture), root, StringComparison.OrdinalIgnoreCase)) return -1;
            return score + 20 - Math.Min(15, picture.Substring(Math.Min(root.Length, picture.Length)).Count(c => c == '\\' || c == '/'));
        }
        internal static void Enrich(JArray items, List<string> files, string scope, CancellationToken token)
        {
            var pictures = files.Where(IsPicture).ToArray();
            var documents = files.Where(p => new[] { ".txt", ".md", ".url" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            var metadata = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.OfType<JObject>())
            {
                token.ThrowIfCancellationRequested(); string path = (string)item["full_path"], root = ProductRoot(path, scope), stem = Path.GetFileNameWithoutExtension(path);
                if (!metadata.TryGetValue(root, out var info))
                {
                    var ids = new HashSet<string>(); var read = new JArray(); var snippets = new List<string>();
                    foreach (string doc in documents.Where(p => Under(p, root)).Take(24))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            if (new FileInfo(doc).Length > 65536) continue;
                            string text = File.ReadAllText(doc, Encoding.UTF8);
                            foreach (Match match in BoothLink.Matches(text)) if (match.Groups[1].Value != "3087170") ids.Add(match.Groups[1].Value);
                            read.Add(doc); if (snippets.Count < 2 && Regex.IsMatch(Path.GetFileName(doc), @"readme|说明|説明", RegexOptions.IgnoreCase)) snippets.Add(text.Substring(0, Math.Min(text.Length, 900)));
                        }
                        catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    info = new JObject { ["product_name"] = Path.GetFileName(root), ["metadata_source"] = read.Count > 0 ? "local_documents" : "file_names",
                        ["metadata_files"] = read, ["description"] = string.Join("\n\n", snippets), ["compatibility_status"] = "unverified" };
                    if (ids.Count == 1) { string id = ids.First(); info["booth_id"] = id; info["booth_url"] = "https://booth.pm/ja/items/" + id; }
                    else if (ids.Count > 1) info["metadata_note"] = "说明含多个商品链接，尚未确认本素材属于哪一个。";
                    else {
                        var hint = Regex.Match(Path.GetFileName(root), @"^(?:\[?booth[ _:-]*)?([0-9]{5,9})(?=\D|$)", RegexOptions.IgnoreCase);
                        DateTime date;
                        if (hint.Success && !DateTime.TryParseExact(hint.Groups[1].Value, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)) {
                            info["booth_id"] = hint.Groups[1].Value; info["booth_url"] = "https://booth.pm/ja/items/" + hint.Groups[1].Value;
                            info["booth_match_source"] = "file_name_hint"; info["metadata_note"] = "商品编号来自文件夹名，关联尚未确认；读取商品信息后请核对名称和图片。";
                        }
                    }
                    metadata[root] = info;
                }
                foreach (var field in info.Properties()) item[field.Name] = field.Value.DeepClone();
                item["name"] = Path.GetFileName(root) == stem ? stem : Path.GetFileName(root) + " · " + stem;
                var siblings = items.OfType<JObject>().Where(x => Path.GetDirectoryName(WorkbenchData.Text(x["full_path"])) == Path.GetDirectoryName(path) && WorkbenchData.Text(x["kind"]) != "plugin").Select(x => Path.GetFileNameWithoutExtension(WorkbenchData.Text(x["full_path"]))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                bool looseDownloads = siblings.Length > 1 && (WorkbenchData.Text(item["kind"]) == "package" || WorkbenchData.Text(item["kind"]) == "archive");
                var candidates = pictures.Where(p => Under(p, root) || string.Equals(Path.GetDirectoryName(p), Path.GetDirectoryName(root), StringComparison.OrdinalIgnoreCase))
                    .Where(p => { string name = NameKey(Path.GetFileNameWithoutExtension(p)); var owners = siblings.Where(s => name == NameKey(s) || NameKey(s).Length >= 4 && name.StartsWith(NameKey(s))).ToArray(); int best = owners.Length == 0 ? 0 : owners.Max(s => NameKey(s).Length); owners = owners.Where(s => NameKey(s).Length == best).ToArray(); return owners.Length == 0 ? !looseDownloads : owners.Length == 1 && owners[0].Equals(stem, StringComparison.OrdinalIgnoreCase); })
                    .Select(p => new { Path = p, Score = PictureScore(p, root, stem) })
                    .Where(p => p.Score >= 0 && (Under(p.Path, root) || p.Score >= 150)).OrderByDescending(p => p.Score).ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).Take(12).Select(p => p.Path).ToArray();
                item["covers"] = new JArray(candidates); item["thumbnail"] = candidates.FirstOrDefault() ?? "";
                item["cover_source"] = candidates.Length > 0 ? "local_picture" : "";
                item["cover_records"] = new JArray(candidates.Select(p => new JObject { ["path"] = p, ["source"] = "local_picture" }));
            }
        }
        // Read the user's selected database, not its service credentials, settings or downloader state.
        internal static JObject ReadMio(string file, string project, CancellationToken token, WorkbenchSources.Progress progress)
        {
            file = Path.GetFullPath(file); var db = ReadJson(file, 32 * 1024 * 1024);
            if (!(db["assets"] is JArray assets)) throw new IOException("请选择 Mio 的 library.json；此文件没有素材记录。");
            JObject packageCovers = null;
            string packageFile = Path.Combine(Path.GetDirectoryName(file), "pkgcovers.json");
            if (File.Exists(packageFile)) { try { packageCovers = ReadJson(packageFile, 8 * 1024 * 1024)["covers"] as JObject; } catch (IOException) { } catch (JsonException) { } }
            var result = new JArray(); var groups = new Dictionary<string, JObject>(); int raw = 0, skipped = 0; bool limited = false;
            foreach (var asset in assets.OfType<JObject>())
            {
                token.ThrowIfCancellationRequested(); if (++raw > 2000 || result.Count >= 1000) { limited = true; break; }
                Interlocked.Increment(ref progress.Files);
                string key = Clip(asset["key"], 300), boothId = Clip(asset["boothId"], 12);
                var user = (db["user"] as JObject)?[key] as JObject ?? new JObject();
                var links = new List<string>();
                foreach (var location in (asset["locations"] as JArray ?? new JArray()).OfType<JObject>()) AddLocal(links, Clip(location["path"], 2048));
                foreach (string field in new[] { "packages", "archives" }) foreach (string path in (asset[field] as JArray ?? new JArray()).Values<string>().Take(100)) AddLocal(links, path);
                if (links.Count == 0 || ((long?)asset["modelFiles"] ?? 0) == 0 && (asset["packages"] as JArray)?.Count == 0 && (asset["archives"] as JArray)?.Count == 0 && (int?)asset["otherArchives"] != 1 && Clip(asset["category"]) == "贴图") { skipped++; continue; }
                if (!Regex.IsMatch(boothId, @"^[0-9]{5,12}$")) boothId = "";
                string groupKey = boothId != "" && (bool?)user["noGroup"] != true ? "booth:" + boothId : key;
                if (groups.TryGetValue(groupKey, out var existing))
                {
                    var members = (JArray)existing["member_paths"]; foreach (string path in links) if (!members.Values<string>().Contains(path, StringComparer.OrdinalIgnoreCase) && members.Count < 100) members.Add(path);
                    var bases = (JArray)existing["bases"]; foreach (string baseName in (user["bases"] as JArray ?? asset["bases"] as JArray ?? new JArray()).Values<string>().Take(20)) if (!bases.Values<string>().Contains(baseName) && bases.Count < 20) bases.Add(Clip(baseName));
                    existing["variants"] = ((int?)existing["variants"] ?? 1) + 1; continue;
                }
                string primary = links.FirstOrDefault(Directory.Exists) ?? links[0];
                var product = boothId == "" ? null : (db["booth"] as JObject)?[boothId] as JObject;
                string name = Clip(user["name"] ?? product?["name"] ?? asset["name"] ?? asset["rawName"]);
                var covers = new List<string>(); string origin = "";
                foreach (var cover in new[] { Tuple.Create(Clip(user["cover"], 2048), "mio_user_picture"), Tuple.Create(Clip(product?["cover"], 2048), "mio_booth_cache") })
                    if (IsPicture(cover.Item1) && File.Exists(cover.Item1)) { covers.Add(cover.Item1); if (origin == "") origin = cover.Item2; }
                foreach (string path in (asset["covers"] as JArray ?? new JArray()).Values<string>().Take(24))
                    if (IsCover(path) && File.Exists(path) && !covers.Contains(path, StringComparer.OrdinalIgnoreCase)) { covers.Add(path); if (origin == "") origin = "mio_local_picture"; }
                var packagePreview = packageCovers?[key] as JObject;
                if (covers.Count == 0 && packagePreview != null && (bool?)packagePreview["off"] != true) {
                    string cacheRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), "covers", "unitypackage"));
                    string previewFile = Clip(packagePreview["file"], 300);
                    if (!string.IsNullOrEmpty(previewFile) && Path.GetFileName(previewFile) == previewFile) {
                        string previewPath = Path.GetFullPath(Path.Combine(cacheRoot, previewFile));
                        if (Under(previewPath, cacheRoot) && IsPicture(previewPath) && File.Exists(previewPath)) { covers.Add(previewPath); origin = "mio_package_preview"; }
                    }
                }
                var entry = new JObject { ["id"] = "mio-" + WorkbenchData.Hash(Encoding.UTF8.GetBytes(file.ToLowerInvariant() + "|" + groupKey)).Substring(0, 16),
                    ["name"] = name, ["product_name"] = name, ["full_path"] = primary, ["path"] = WorkbenchSources.AssetPath(primary, project),
                    ["kind"] = Directory.Exists(primary) ? "folder" : Path.GetExtension(primary).Equals(".unitypackage", StringComparison.OrdinalIgnoreCase) ? "package" : "archive",
                    ["bytes"] = (long?)asset["size"] ?? 0, ["state"] = "local_only", ["category"] = Clip(user["category"] ?? asset["category"]),
                    ["bases"] = new JArray((user["bases"] as JArray ?? asset["bases"] as JArray ?? new JArray()).Values<string>().Take(20).Select(x => Clip(x))),
                    ["tags"] = new JArray((user["tags"] as JArray ?? product?["tags"] as JArray ?? new JArray()).Values<string>().Take(20).Select(x => Clip(x))),
                    ["booth_id"] = boothId, ["booth_url"] = boothId == "" ? "" : "https://booth.pm/ja/items/" + boothId,
                    ["description"] = Clip(product?["desc"] ?? user["notes"], 1800), ["shop"] = Clip(product?["shop"]),
                    ["covers"] = new JArray(covers.Take(12)), ["thumbnail"] = covers.FirstOrDefault() ?? "", ["cover_source"] = origin,
                    ["metadata_source"] = "mio_library", ["metadata_file"] = file, ["source_key"] = key, ["compatibility_status"] = "unverified",
                    ["package_preview_entry"] = origin == "mio_package_preview" ? Clip(packagePreview?["entry"], 400) : "",
                    ["member_paths"] = new JArray(links.Take(100)), ["variants"] = 1 };
                entry["cover_records"] = new JArray(covers.Take(12).Select(p => new JObject { ["path"] = p, ["source"] = origin == "mio_package_preview" ? origin : p == Clip(user["cover"], 2048) ? "mio_user_picture" : p == Clip(product?["cover"], 2048) ? "mio_booth_cache" : "mio_local_picture" }));
                groups[groupKey] = entry; result.Add(entry); Interlocked.Increment(ref progress.Found);
            }
            return new JObject { ["items"] = result, ["raw_records"] = assets.Count, ["files_read"] = Math.Min(raw, 2000), ["directories_read"] = 0, ["skipped"] = skipped, ["limited"] = limited, ["read_at"] = WorkbenchData.Now };
        }
        static void AddLocal(List<string> paths, string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || path.Length > 2048) return;
                path = Path.GetFullPath(path);
                if (path.TrimEnd('\\', '/') == Path.GetPathRoot(path).TrimEnd('\\', '/') || !File.Exists(path) && !Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
        }
        internal static string CoverLabel(JObject item)
        {
            switch (WorkbenchData.Text(item["cover_source"]))
            {
                case "mio_user_picture": return "Mio 自选图片"; case "mio_booth_cache": return "Mio 商品封面缓存";
                case "mio_local_picture": return "Mio 本地已有图片"; case "local_picture": return "本地已有图片";
                case "mio_package_preview": return "Mio 包内 Unity 缩略图";
                case "booth_original": return "BOOTH 商品原图"; case "booth_thumbnail": return "BOOTH 压缩缩略图";
                case "unknown_picture": return "已有图片（来源类型未记录）";
                case "package_preview": return "包内 Unity 缩略图"; default: return WorkbenchData.Text(item["kind"]) == "prefab" ? "Unity 预览（非商品图）" : "没有商品封面";
            }
        }
        internal static string PictureLabel(JObject item, string path)
        {
            var entry = (item["cover_records"] as JArray)?.OfType<JObject>().FirstOrDefault(x => WorkbenchData.Text(x["path"]) == path);
            if (entry != null) return CoverLabel(new JObject { ["cover_source"] = entry["source"]?.DeepClone() });
            return path == WorkbenchData.Text(item["thumbnail"]) ? CoverLabel(item) : "已有图片（来源类型未记录）";
        }
        internal static JArray PictureRecords(JObject item)
        {
            if (item["cover_records"] is JArray records) return (JArray)records.DeepClone();
            return new JArray((item["covers"] as JArray ?? new JArray()).Values<string>().Select(path => new JObject {
                ["path"] = path, ["source"] = path == WorkbenchData.Text(item["thumbnail"]) ? WorkbenchData.Text(item["cover_source"]) : "unknown_picture" }));
        }
        internal static void PreserveFetched(JArray before, JArray after)
        {
            if (before == null || after == null) return;
            var old = before.OfType<JObject>().GroupBy(x => WorkbenchData.Text(x["id"])).ToDictionary(x => x.Key, x => x.First());
            foreach (var item in after.OfType<JObject>()) {
                if (!old.TryGetValue(WorkbenchData.Text(item["id"]), out var previous) || WorkbenchData.Text(previous["metadata_source"]) != "booth_item" ||
                    WorkbenchData.Text(previous["booth_id"]) != WorkbenchData.Text(item["booth_id"]) || WorkbenchData.Text(previous["full_path"]) != WorkbenchData.Text(item["full_path"])) continue;
                foreach (string field in new[] { "booth_name", "booth_category", "description", "shop", "tags", "metadata_source", "metadata_read_at" }) if (previous[field] != null) item[field] = previous[field].DeepClone();
                var images = PictureRecords(previous).OfType<JObject>().Where(x => File.Exists(WorkbenchData.Text(x["path"]))).ToList();
                foreach (var image in PictureRecords(item).OfType<JObject>()) if (!images.Any(x => WorkbenchData.Text(x["path"]) == WorkbenchData.Text(image["path"]))) images.Add((JObject)image.DeepClone());
                if (images.Count == 0) continue;
                item["cover_records"] = new JArray(images.Take(12)); item["covers"] = new JArray(images.Take(12).Select(x => WorkbenchData.Text(x["path"])));
                item["thumbnail"] = images[0]["path"].DeepClone(); item["cover_source"] = images[0]["source"].DeepClone();
            }
        }
    }
}

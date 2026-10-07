using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AvatarWorkbench
{
    internal static class WorkbenchCoverImage
    {
        internal static string Fingerprint(string path)
        {
            try { var f = new FileInfo(path); return path + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks; }
            catch (IOException) { return path + "|missing"; } catch (UnauthorizedAccessException) { return path + "|denied"; }
        }
        internal static void Validate(byte[] data)
        {
            int width = 0, height = 0;
            if (data.Length >= 24 && data[0] == 137 && data[1] == 80 && data[2] == 78 && data[3] == 71)
            {
                width = Big32(data, 16); height = Big32(data, 20);
            }
            else if (data.Length > 4 && data[0] == 255 && data[1] == 216)
            {
                int p = 2;
                while (p + 4 < data.Length)
                {
                    if (data[p++] != 255) break;
                    while (p < data.Length && data[p] == 255) p++;
                    if (p >= data.Length) break; int marker = data[p++];
                    if (marker == 216 || marker == 217 || marker == 1 || marker >= 208 && marker <= 215) continue;
                    if (p + 2 > data.Length) break;
                    int length = data[p] * 256 + data[p + 1]; if (length < 2 || p + length > data.Length) break;
                    if (new[] { 192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207 }.Contains(marker) && length >= 8)
                    { height = data[p + 3] * 256 + data[p + 4]; width = data[p + 5] * 256 + data[p + 6]; break; }
                    if (marker == 218) break; p += length;
                }
            }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 16 * 1024 * 1024) throw new IOException("图片尺寸无效或过大；支持 PNG / JPEG，最大 8192 边长、16M 像素。");
        }
        static int Big32(byte[] data, int p) => (data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3];
        internal static Texture2D Load(string path, int edge)
        {
            if (!WorkbenchCatalogMetadata.IsPicture(path) || !File.Exists(path)) throw new IOException("商品图片不存在或格式不受支持：" + path);
            if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new IOException("商品图片超过 20 MiB。");
            byte[] data = File.ReadAllBytes(path); Validate(data);
            var original = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "AW_Cover" };
            if (!original.LoadImage(data)) { UnityEngine.Object.DestroyImmediate(original); throw new IOException("商品图片无法解码。"); }
            if (Math.Max(original.width, original.height) <= edge) return original;
            float ratio = (float)edge / Math.Max(original.width, original.height);
            int w = Math.Max(1, Mathf.RoundToInt(original.width * ratio)), h = Math.Max(1, Mathf.RoundToInt(original.height * ratio));
            RenderTexture previous = RenderTexture.active, rt = null; Texture2D result = null;
            try
            {
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32); Graphics.Blit(original, rt); RenderTexture.active = rt;
                result = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "AW_Cover" };
                result.ReadPixels(new Rect(0, 0, w, h), 0, 0); result.Apply(false, true); return result;
            }
            catch { if (result) UnityEngine.Object.DestroyImmediate(result); throw; }
            finally { RenderTexture.active = previous; if (rt) RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(original); }
        }
    }
}

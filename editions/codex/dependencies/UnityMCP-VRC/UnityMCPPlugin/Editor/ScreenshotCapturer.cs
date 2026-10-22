using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UnityMCP.Editor
{
    public class ScreenshotCapturer
    {
        public class ScreenshotData
        {
            public string camera { get; set; }   // "scene" (default) or "game"
            public int? width { get; set; }       // default 1280
            public int? height { get; set; }      // default 720
            public string format { get; set; }    // "jpg" (default) or "png"
        }

        // Captures a screenshot and returns it as the payload for a "screenshot" response
        // ({ base64, format }, or { error } on failure). The connection layer serializes this,
        // echoing the request id, back to the requesting client. Rendering touches Unity APIs, so it
        // runs on the main thread via RunOnMainThread, which defers while the Editor is compiling and
        // only times out if the Editor stays unfocused past its limit. On failure we return an
        // { error } payload so the client fails fast with a useful message rather than hanging.
        public async Task<object> GetScreenshotData(string dataJson)
        {
            try
            {
                var opts = string.IsNullOrEmpty(dataJson)
                    ? new ScreenshotData()
                    : (JsonConvert.DeserializeObject<ScreenshotData>(dataJson) ?? new ScreenshotData());

                // Deconstruct positionally: tuple element names don't survive generic inference.
                var (base64, format) = await EditorUtilities
                    .RunOnMainThread(() => CaptureAndEncode(opts))
                    .ConfigureAwait(false);

                return new { base64, format };
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] Error capturing screenshot: {e.Message}");
                return new { error = e.Message };
            }
        }

        private (string base64, string format) CaptureAndEncode(ScreenshotData opts)
        {
            string mode = string.IsNullOrEmpty(opts?.camera) ? "scene" : opts.camera.ToLowerInvariant();
            int width = (opts?.width).GetValueOrDefault() > 0 ? opts.width.Value : 1280;
            int height = (opts?.height).GetValueOrDefault() > 0 ? opts.height.Value : 720;
            bool usePng = !string.IsNullOrEmpty(opts?.format) && opts.format.ToLowerInvariant() == "png";

            Camera cam = ResolveCamera(mode);
            if (cam == null)
            {
                throw new Exception("No camera available to capture (no active Scene view and no camera in the scene).");
            }

            // Render into an MSAA target, then resolve into a plain RT for ReadPixels.
            RenderTexture rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            RenderTexture resolved = RenderTexture.GetTemporary(width, height, 0);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture prevCamTarget = cam.targetTexture;
            Texture2D tex = null;

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                Graphics.Blit(rt, resolved);
                RenderTexture.active = resolved;

                tex = new Texture2D(width, height, usePng ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] bytes = usePng ? tex.EncodeToPNG() : tex.EncodeToJPG(75);
                return (Convert.ToBase64String(bytes), usePng ? "png" : "jpg");
            }
            finally
            {
                cam.targetTexture = prevCamTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(resolved);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (tex != null)
                {
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
        }

        private Camera ResolveCamera(string mode)
        {
            Camera cam = null;
            if (mode == "game")
            {
                cam = Camera.main;
                if (cam == null) cam = UnityEngine.Object.FindObjectOfType<Camera>();
                if (cam == null) cam = SceneViewCamera();
            }
            else
            {
                // "scene" (default): prefer what the developer is looking at, fall back to a game camera.
                cam = SceneViewCamera();
                if (cam == null) cam = Camera.main;
                if (cam == null) cam = UnityEngine.Object.FindObjectOfType<Camera>();
            }
            return cam;
        }

        private Camera SceneViewCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                sv.Repaint();
                return sv.camera;
            }
            return null;
        }
    }
}

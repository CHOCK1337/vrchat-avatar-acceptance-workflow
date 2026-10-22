using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarWorkbench
{
    public sealed partial class WorkbenchWindow
    {
        // A narrow bridge to the same controls used by the window. It never changes
        // wardrobe parameters, asset contents, consumer leases or feedback state.
        public static string RunPreviewCommand(string json)
        {
            WorkbenchWindow window = null;
            try
            {
                var request = JObject.Parse(json);
                string project = (string)request["project"];
                if (string.IsNullOrEmpty(project) || !string.Equals(Path.GetFullPath(project).TrimEnd('/', '\\'), WorkbenchData.Project.TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PROJECT_MISMATCH: rediscover the exact Unity project.");
                var windows = Resources.FindObjectsOfTypeAll<WorkbenchWindow>();
                if (windows.Length != 1) throw new InvalidOperationException("WORKBENCH_NOT_UNIQUE: open exactly one existing workbench window.");
                window = windows[0];
                window.ReadPreviewControls();
                string action = (string)request["action"] ?? "inspect";
                if (action == "inspect") {
                    var state = window.PreviewCommandState(true);
                    string part = (string)request["root_path"];
                    if (!string.IsNullOrEmpty(part)) {
                        state["source_part"] = PreviewPart(window.target, part);
                        state["preview_part"] = window.preview?.InspectPart(part);
                    }
                    return state.ToString(Newtonsoft.Json.Formatting.None);
                }
                if (action != "select" && action != "capture") throw new ArgumentException("UNKNOWN_ACTION: inspect, select or capture.");
                window.GuardPreviewCommand(request);
                window.SelectPanel("preview"); window.Show(); window.Focus(); window.busy = window.IsBusy();
                if (action == "select")
                {
                    string outfit = (string)request["outfit"], pose = (string)request["pose"];
                    if (string.IsNullOrEmpty(outfit) == string.IsNullOrEmpty(pose)) throw new ArgumentException("SELECT_ONE_CONTROL: choose outfit OR pose, then allow normal Editor updates.");
                    if (pose != null) window.PreviewPose(pose);
                    else
                    {
                        var matches = window.outfitOptions.Select((c, i) => new { c, i }).Where(x => x.c.Name == outfit).ToArray();
                        if (matches.Length != 1) throw new ArgumentException("OUTFIT_NOT_UNIQUE: use an exact name from inspect.options.");
                        window.PreviewOutfit(matches[0].i);
                    }
                    var receipt = window.PreviewCommandState(false);
                    receipt["next"] = "Wait for normal Editor updates, then capture. Control submission is not visual acceptance.";
                    return receipt.ToString(Newtonsoft.Json.Formatting.None);
                }
                if (window.preview?.Gesture == null) throw new InvalidOperationException("NO_FUNCTIONAL_PREVIEW: select a pose, then an outfit first.");
                if (window.preview.Gesture.Moving) throw new InvalidOperationException("PREVIEW_SETTLING: wait for normal Editor updates; do not loop Tick synchronously.");
                string focus = (string)request["focus"] ?? "全身";
                if (!new[] { "全身", "头部", "上身", "鞋子" }.Contains(focus)) throw new ArgumentException("UNKNOWN_FOCUS: 全身, 头部, 上身 or 鞋子.");
                window.preview.SetFocus(focus);
                window.preview.Render(new Rect(0, 0, 960, 1080));
                var texture = window.preview.Capture();
                string directory = Path.Combine(WorkbenchData.Space(window.BindingPath), "preview-evidence");
                string image = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
                try { Directory.CreateDirectory(directory); File.WriteAllBytes(image, texture.EncodeToPNG()); }
                finally { if (texture) UnityEngine.Object.DestroyImmediate(texture); }
                var result = window.PreviewCommandState(true);
                result["image"] = image; result["image_sha256"] = WorkbenchData.FileHash(image);
                result["rendered_controls"] = window.CompactPreviewControls(window.preview.RenderedControls);
                result["view"] = window.preview.RenderedView?.DeepClone();
                result["visual_acceptance"] = "NOT_EVALUATED: open the original image; geometry and control state are supporting evidence only.";
                File.WriteAllText(Path.ChangeExtension(image, ".json"), result.ToString());
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception e)
            {
                while (e.InnerException != null) e = e.InnerException;
                var result = new JObject { ["ok"] = false, ["error"] = e.Message };
                if (window) { try { result["state"] = window.PreviewCommandState(false); } catch { } }
                return result.ToString(Newtonsoft.Json.Formatting.None);
            }
        }

        void GuardPreviewCommand(JObject request)
        {
            if (!target || !EditorUtility.IsPersistent(target)) throw new InvalidOperationException("PERSISTENT_CANDIDATE_REQUIRED: bind the exact processed candidate in the existing window.");
            string path = AssetDatabase.GetAssetPath(target), expected = (string)request["expected_asset_path"];
            if (path != expected || AssetDatabase.GetAssetDependencyHash(path).ToString() != (string)request["expected_dependency_hash"])
                throw new InvalidOperationException("CANDIDATE_CHANGED: inspect and verify the current candidate before operating it.");
            var consumer = WorkbenchData.Consumer(BindingPath);
            string session = (string)request["session"];
            if (string.IsNullOrEmpty(session) || consumer == null || (string)consumer["session"] != session || (bool?)consumer["active"] != true)
                throw new InvalidOperationException("PREVIEW_LEASE_REQUIRED: connect your own active consumer lease first.");
            if ((bool?)consumer["busy"] == true)
                throw new InvalidOperationException("PREVIEW_LEASE_BUSY: set YOUR consumer_lease to idle, retaining active ownership. busy pauses preview controls and animation.");
            if (IsBusy()) throw new InvalidOperationException("EDITOR_BUSY: wait for compilation/import/play to finish.");
            if (frozen || historical) throw new InvalidOperationException("FROZEN_OR_HISTORY: return to the live model without discarding user feedback.");
            if (!string.IsNullOrEmpty(controlsUnavailable)) throw new InvalidOperationException(controlsUnavailable);
        }

        JObject PreviewCommandState(bool geometry)
        {
            string path = target ? AssetDatabase.GetAssetPath(target) : "";
            var result = new JObject {
                ["ok"] = true, ["project"] = WorkbenchData.Project, ["binding"] = BindingPath,
                ["target"] = target ? target.name : "", ["source_kind"] = sourceKind,
                ["asset_path"] = path, ["dependency_hash"] = path == "" ? "" : AssetDatabase.GetAssetDependencyHash(path).ToString(),
                ["busy"] = IsBusy(), ["consumer"] = WorkbenchData.Consumer(BindingPath),
                ["frozen"] = frozen != null, ["historical"] = historical != null, ["visible"] = windowVisible,
                ["controls_unavailable"] = controlsUnavailable, ["scene_dirty"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,
                ["options"] = new JArray(outfitOptions.Select(c => new JObject { ["name"] = c.Name, ["path"] = c.Path, ["parameter"] = c.Parameter, ["value"] = c.Value })),
                ["controls"] = CompactPreviewControls(preview?.Gesture?.State())
            };
            if (geometry) {
                result["source_geometry"] = PreviewGeometry(target, true);
                result["preview_geometry"] = preview?.InspectGeometry();
            }
            return result;
        }

        JObject CompactPreviewControls(JObject controls)
        {
            if (controls == null) return null;
            var result = new JObject();
            foreach (string key in new[] { "outfit", "pose", "settled", "animation_sample_time", "physics_running", "scope" }) result[key] = controls[key]?.DeepClone();
            result["wardrobe_values"] = new JObject(outfitOptions.Select(c => c.Parameter).Distinct().Select(p => new JProperty(p, controls["parameters"]?[p]?.DeepClone())));
            return result;
        }

        internal static JObject PreviewGeometry(GameObject root, bool sources = false)
        {
            if (!root) return null;
            var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var groups = skins.GroupBy(r => AnimationUtility.CalculateTransformPath(r.transform, root.transform).Split('/')[0]);
            var body = skins.FirstOrDefault(r => r.name == "Body_base");
            return new JObject {
                ["groups"] = new JArray(groups.Select(g => new JObject {
                    ["root"] = g.Key, ["renderers"] = g.Count(), ["visible"] = g.Count(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh && r.sharedMesh.vertexCount > 0),
                    ["vertices"] = g.Sum(r => r.sharedMesh ? r.sharedMesh.vertexCount : 0),
                    ["mesh_assets"] = sources ? new JArray(g.Where(r => r.sharedMesh).Select(r => AssetDatabase.GetAssetPath(r.sharedMesh)).Distinct()) : null
                })),
                ["body_shapes"] = body && body.sharedMesh ? new JArray(Enumerable.Range(0, body.sharedMesh.blendShapeCount).Where(i => new[] {"Foot", "Toe", "Lower_leg", "Knee", "Upper_leg"}.Contains(body.sharedMesh.GetBlendShapeName(i))).Select(i => new JObject { ["name"] = body.sharedMesh.GetBlendShapeName(i), ["value"] = body.GetBlendShapeWeight(i) })) : null
            };
        }

        internal static JObject PreviewPart(GameObject root, string path)
        {
            if (!root) return new JObject { ["found"] = false, ["reason"] = "preview not initialized" };
            var part = root.transform.Find(path);
            if (!part) return new JObject { ["found"] = false, ["root_path"] = path, ["reason"] = "not found under this exact source; no scene-wide fallback" };
            var body = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.name == "Body_base");
            var bodyBones = new System.Collections.Generic.HashSet<Transform>(body ? body.bones : new Transform[0]);
            var skins = part.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshFilters = part.GetComponentsInChildren<MeshFilter>(true).Where(m => m.GetComponent<MeshRenderer>()).ToArray();
            return new JObject { ["found"] = true, ["root_path"] = path,
                ["renderers"] = part.GetComponentsInChildren<Renderer>(true).Length, ["skinned_renderers"] = skins.Length,
                ["static_meshes"] = new JArray(meshFilters.Select(m => new JObject {
                    ["path"] = AnimationUtility.CalculateTransformPath(m.transform, root.transform),
                    ["mesh"] = m.sharedMesh ? AssetDatabase.GetAssetPath(m.sharedMesh) : "", ["vertices"] = m.sharedMesh ? m.sharedMesh.vertexCount : 0,
                    ["active"] = m.gameObject.activeInHierarchy, ["enabled"] = m.GetComponent<MeshRenderer>().enabled
                })),
                ["skins"] = new JArray(skins.Select(s => new JObject {
                    ["path"] = AnimationUtility.CalculateTransformPath(s.transform, root.transform),
                    ["active"] = s.gameObject.activeInHierarchy, ["enabled"] = s.enabled,
                    ["mesh"] = s.sharedMesh ? AssetDatabase.GetAssetPath(s.sharedMesh) : "", ["vertices"] = s.sharedMesh ? s.sharedMesh.vertexCount : 0,
                    ["bones"] = s.bones.Length, ["null_bones"] = s.bones.Count(b => !b), ["body_shared_bones"] = s.bones.Count(b => b && bodyBones.Contains(b)),
                    ["root_bone"] = s.rootBone ? AnimationUtility.CalculateTransformPath(s.rootBone, root.transform) : "",
                    ["bounds_size"] = new JArray(s.bounds.size.x, s.bounds.size.y, s.bounds.size.z)
                })) };
        }
    }
}

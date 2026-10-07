using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    /// <summary>Render-only snapshot: no author scripts, animator, constraints or source writes.</summary>
    internal sealed class WorkbenchPreview : IDisposable
    {
        PreviewRenderUtility utility;
        GameObject clone;
        readonly Dictionary<Transform, Transform> bones = new Dictionary<Transform, Transform>();
        Bounds bounds;
        WorkbenchGesturePreview gesture;
        readonly List<Material> temporaryMaterials = new List<Material>();
        public WorkbenchGesturePreview Gesture => gesture;
        public JObject RenderedControls { get; private set; }
        Scene simulationScene;
        GameObject simulationRoot;
        readonly List<KeyValuePair<Renderer, Renderer>> liveRenderers = new List<KeyValuePair<Renderer, Renderer>>();
        JObject sampledControls;
        public Texture Frame { get; private set; }
        public JObject RenderedView { get; private set; }
        public int RenderCount { get; private set; }
        public int RendererCount { get; private set; }
        public int VisibleRendererCount { get; private set; }
        public string Error { get; private set; }
        public string SourceNotice { get; private set; }
        public int SceneProxyCount { get; private set; }
        public float Yaw = 0, Pitch = 4, Distance = 3;
        public Vector3 Pivot;
        public bool Dirty = true;
        public string FocusName = "全身";
        public Vector2 LastSize;
        bool autoFrame = true;
        float renderedDistance;

        public void Load(GameObject source)
        {
            Dispose(); Error = null; bones.Clear();
            try
            {
                CreateUtility();
                clone = EditorUtility.CreateGameObjectWithHideFlags("AW_PreviewRoot", HideFlags.HideAndDontSave);
                utility.AddSingleGO(clone);
                CopyTree(source.transform, clone.transform, true);
                var sceneProxies = WorkbenchScenePreview.Capture(source, out string notice);
                SourceNotice = notice; SceneProxyCount = sceneProxies.Count;
                foreach (var pair in bones)
                {
                    var sourceTransform = pair.Key; var dest = pair.Value.gameObject;
                    var original = sourceTransform.GetComponent<Renderer>();
                    Renderer shown = original;
                    if (original && sceneProxies.TryGetValue(original, out var proxy)) shown = proxy;
                    var skin = shown as SkinnedMeshRenderer;
                    if (skin)
                    {
                        var copy = dest.AddComponent<SkinnedMeshRenderer>();
                        EditorUtility.CopySerialized(skin, copy);
                        var mapped = new Transform[skin.bones.Length];
                        for (int i = 0; i < mapped.Length; i++)
                        {
                            var bone = skin.bones[i];
                            if (bone != null && !bones.TryGetValue(bone, out mapped[i]))
                                throw new InvalidOperationException("所选对象的蒙皮引用角色外部骨骼，请选择完整 Avatar 根对象。");
                        }
                        copy.bones = mapped;
                        if (skin.rootBone && !bones.ContainsKey(skin.rootBone))
                            throw new InvalidOperationException("RootBone 位于当前选择之外，请选择完整角色根对象。");
                        copy.rootBone = skin.rootBone ? bones[skin.rootBone] : null;
                        copy.updateWhenOffscreen = true;
                        copy.forceMatrixRecalculationPerRender = true;
                        CopyVisibilityAndProperties(original, shown, copy);
                        liveRenderers.Add(new KeyValuePair<Renderer, Renderer>(shown, copy));
                    }
                    var mf = shown ? shown.GetComponent<MeshFilter>() : null;
                    var mr = shown as MeshRenderer;
                    if (mf && mr)
                    {
                        EditorUtility.CopySerialized(mf, dest.AddComponent<MeshFilter>());
                        var copy = dest.AddComponent<MeshRenderer>();
                        EditorUtility.CopySerialized(mr, copy);
                        CopyVisibilityAndProperties(original, shown, copy);
                        liveRenderers.Add(new KeyValuePair<Renderer, Renderer>(shown, copy));
                    }
                }
                var renderers = clone.GetComponentsInChildren<Renderer>(true);
                RendererCount = renderers.Length; VisibleRendererCount = 0;
                bool first = true;
                foreach (var r in renderers)
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    VisibleRendererCount++;
                    if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds);
                }
                if (first) throw new InvalidOperationException("所选对象当前没有可见的网格。请选择完整角色；不会擅自打开隐藏物件。");
                SetFocus("全身");
            }
            catch (Exception e) { Dispose(); Error = e.Message; throw; }
        }

        void CreateUtility()
        {
            utility = new PreviewRenderUtility();
            utility.camera.cameraType = CameraType.Preview;
            utility.camera.fieldOfView = 30;
            utility.camera.clearFlags = CameraClearFlags.SolidColor;
            utility.camera.backgroundColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? new Color(.12f, .14f, .19f).linear : new Color(.12f, .14f, .19f);
            utility.camera.allowHDR = false; utility.camera.cullingMask = ~0;
            utility.lights[0].intensity = 1.15f; utility.lights[0].transform.rotation = Quaternion.Euler(35, -25, 0);
            utility.lights[1].intensity = .6f; utility.lights[1].transform.rotation = Quaternion.Euler(345, 150, 0);
            utility.ambientColor = new Color(.65f, .65f, .65f);
        }

        public void LoadFunctional(GameObject source, List<WorkbenchMenuControl> menu)
        {
            string unavailable = WorkbenchGesturePreview.Availability(source);
            if (!string.IsNullOrEmpty(unavailable)) throw new InvalidOperationException(unavailable);
            Dispose();
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject nativeRoot = null;
            var nativeGesture = new WorkbenchGesturePreview();
            var ownedMaterials = new List<Material>();
            try
            {
                nativeRoot = Object.Instantiate(source); nativeRoot.name = "AW_GestureAvatar";
                WorkbenchGesturePreview.HideTree(nativeRoot); SceneManager.MoveGameObjectToScene(nativeRoot, scene);
                nativeRoot.transform.position = Vector3.zero; nativeRoot.transform.rotation = source.transform.rotation; nativeRoot.transform.localScale = source.transform.lossyScale;
                // Exact material copies protect shared assets from animation property changes.
                // Shader, alpha masks, normals and all author values are retained.
                var materials = new Dictionary<Material, Material>();
                var renderers = nativeRoot.GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    renderer.sharedMaterials = Array.ConvertAll(renderer.sharedMaterials, original =>
                    {
                        if (!original) return null;
                        if (!materials.TryGetValue(original, out var copy)) { copy = new Material(original) { hideFlags = HideFlags.HideAndDontSave, name = "AW_" + original.name }; materials[original] = copy; ownedMaterials.Add(copy); }
                        return copy;
                    });
                    if (renderer is SkinnedMeshRenderer skin) { skin.updateWhenOffscreen = true; skin.forceMatrixRecalculationPerRender = true; }
                }
                nativeGesture.Connect(nativeRoot, scene, menu);
                // Unity's editor humanoid skinning cache can render an earlier pose even
                // after the native graph has moved its bones. Render the actual evaluated
                // transforms in our existing render-only tree, without a second Animator.
                // The mapping is built once. Unity recalculates GPU skin matrices per
                // preview render; continuous motion requires no CPU BakeMesh.
                Load(nativeRoot);
                temporaryMaterials.AddRange(ownedMaterials);
                simulationScene = scene; simulationRoot = nativeRoot; gesture = nativeGesture;
                SampleGesturePose();
                SourceNotice = "Unity 临时动画预览 · 可开启 PhysBone · 非最终构建"; SetFocus("全身");
            }
            catch { nativeGesture.Dispose(); if (nativeRoot) Object.DestroyImmediate(nativeRoot); if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene); Dispose(); foreach (var material in ownedMaterials) if (material) Object.DestroyImmediate(material); throw; }
        }
        public void StopGesture() { gesture?.Dispose(); gesture = null; }

        public void SyncGestureOutput()
        {
            if (gesture == null) return;
            foreach (var pair in bones)
            {
                if (!pair.Key || !pair.Value) continue;
                pair.Value.localPosition = pair.Key.localPosition;
                pair.Value.localRotation = pair.Key.localRotation; pair.Value.localScale = pair.Key.localScale;
            }
            foreach (var pair in liveRenderers)
            {
                if (!pair.Key || !pair.Value) continue;
                CopyVisibilityAndProperties(pair.Key, pair.Key, pair.Value);
                var materials = pair.Key.sharedMaterials;
                var previous = pair.Value.sharedMaterials;
                if (!materials.SequenceEqual(previous)) pair.Value.sharedMaterials = materials;
                if (pair.Key is SkinnedMeshRenderer sourceSkin && pair.Value is SkinnedMeshRenderer shownSkin && sourceSkin.sharedMesh)
                    for (int i = 0; i < sourceSkin.sharedMesh.blendShapeCount; i++) shownSkin.SetBlendShapeWeight(i, sourceSkin.GetBlendShapeWeight(i));
            }
            Dirty = true;
        }

        public void SampleGesturePose()
        {
            if (gesture == null) return;
            SyncGestureOutput();
            sampledControls = gesture.State(); sampledControls["preview_output"] = "unity_native_gpu_skinning";
            Dirty = true;
        }

        void CopyTree(Transform source, Transform dest, bool root)
        {
            bones[source] = dest;
            dest.name = root ? "AW_PreviewRoot" : source.name;
            dest.gameObject.layer = source.gameObject.layer;
            dest.localPosition = root ? Vector3.zero : source.localPosition;
            dest.localRotation = root ? source.rotation : source.localRotation;
            dest.localScale = root ? source.lossyScale : source.localScale;
            // Only renderers are copied. Visibility comes from the actual source/NDMF proxy,
            // so an enabled proxy under an inactive author object can be shown faithfully.
            dest.gameObject.SetActive(true);
            foreach (Transform child in source)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags(child.name, HideFlags.HideAndDontSave);
                go.transform.SetParent(dest, false);
                CopyTree(child, go.transform, false);
            }
        }

        static bool SourceVisible(Renderer renderer)
        {
            if (!renderer || !renderer.enabled) return false;
            if (!EditorUtility.IsPersistent(renderer)) return renderer.gameObject.activeInHierarchy;
            for (var t = renderer.transform; t; t = t.parent) if (!t.gameObject.activeSelf) return false;
            return true;
        }
        static void CopyVisibilityAndProperties(Renderer original, Renderer shown, Renderer copy)
        {
            copy.enabled = SourceVisible(shown) && (EditorUtility.IsPersistent(original) || !SceneVisibilityManager.instance.IsHidden(original.gameObject));
            // NDMF suppresses proxies outside its camera callback. That transient flag is not
            // a menu state; the isolated copy uses the proxy's real enabled state.
            copy.forceRenderingOff = false;
            var block = new MaterialPropertyBlock(); shown.GetPropertyBlock(block); copy.SetPropertyBlock(block);
            for (int i = 0; i < shown.sharedMaterials.Length; i++)
            {
                block.Clear(); shown.GetPropertyBlock(block, i); copy.SetPropertyBlock(block, i);
            }
        }

        public void SetFocus(string region)
        {
            FocusName = region; autoFrame = true; renderedDistance = 0;
            float height = Mathf.Max(bounds.size.y, .1f);
            Pivot = bounds.center;
            float extent = Mathf.Max(bounds.size.y, bounds.size.x);
            if (region == "头部") { Pivot.y = bounds.max.y - height * .12f; extent = height * .32f; }
            else if (region == "上身") { Pivot.y = bounds.min.y + height * .68f; extent = height * .62f; }
            else if (region == "鞋子") { Pivot.y = bounds.min.y + height * .09f; extent = height * .28f; }
            Distance = Mathf.Max(.12f, extent * .6f / Mathf.Tan(15 * Mathf.Deg2Rad));
            Yaw = 0; Pitch = region == "鞋子" ? 10 : 4; Dirty = true;
        }
        public void Orient(string direction)
        {
            Yaw = direction == "侧面" ? 90 : direction == "背面" ? 180 : 0;
            Pitch = 4; Dirty = true;
        }
        public void Orbit(Vector2 delta) { Yaw -= delta.x * .45f; Pitch = Mathf.Clamp(Pitch + delta.y * .3f, -80, 80); Dirty = true; }
        public void Zoom(float delta)
        {
            // Once the user zooms, do not reapply the whole-body fitting minimum every frame.
            float start = autoFrame && renderedDistance > 0 ? renderedDistance : Distance;
            autoFrame = false;
            Distance = Mathf.Clamp(start * Mathf.Exp(delta * .055f), .08f, 50); Dirty = true;
        }
        public void Pan(Vector2 delta)
        {
            var orientation = Quaternion.Euler(Pitch, Yaw, 0);
            Pivot += orientation * new Vector3(-delta.x, delta.y, 0) * Distance * .0015f; Dirty = true;
        }
        float CameraDistance => autoFrame && renderedDistance > 0 ? renderedDistance : Distance;
        public Vector3 CameraPosition => Pivot + Quaternion.Euler(Pitch, Yaw, 0) * Vector3.forward * CameraDistance;
        public Vector3 CameraForward => Quaternion.Euler(Pitch, Yaw, 0) * Vector3.back;
        public float CameraMoveSpeed => Mathf.Clamp(CameraDistance * .4f, .2f, 8f);

        void LockCameraDistance()
        {
            // Preserve the distance actually used for wide viewports before leaving auto-fit.
            Distance = CameraDistance; autoFrame = false;
        }
        public void FlyLook(Vector2 delta)
        {
            if (delta.sqrMagnitude < .000001f) return;
            var position = CameraPosition;
            LockCameraDistance();
            Yaw += delta.x * .45f;
            Pitch = Mathf.Clamp(Pitch - delta.y * .3f, -89f, 89f);
            // A fly camera turns in place; its look target moves instead of orbiting the Avatar.
            Pivot = position - Quaternion.Euler(Pitch, Yaw, 0) * Vector3.forward * Distance;
            Dirty = true;
        }
        public void MoveCamera(Vector3 localDisplacement)
        {
            if (localDisplacement.sqrMagnitude < .000000001f) return;
            LockCameraDistance();
            var orientation = Quaternion.Euler(Pitch, Yaw, 0);
            // The camera looks along -orbit Z, so its right is -orbit X. Q/E uses world up.
            Pivot += orientation * new Vector3(-localDisplacement.x, 0, -localDisplacement.z)
                + Vector3.up * localDisplacement.y;
            Dirty = true;
        }
        public void KeepCameraFrom(WorkbenchPreview previous)
        {
            Yaw = previous.Yaw; Pitch = previous.Pitch; Distance = previous.Distance;
            Pivot = previous.Pivot; FocusName = previous.FocusName;
            autoFrame = previous.autoFrame; renderedDistance = previous.renderedDistance; Dirty = true;
        }
        public Texture Render(Rect rect)
        {
            if (utility == null || rect.width < 2 || rect.height < 2) return Frame;
            if (!Dirty && LastSize == rect.size) return Frame;
            // Fit wide full-body poses using the actual viewport aspect ratio.
            float actualDistance = Distance;
            if (autoFrame && FocusName == "全身")
                actualDistance = Mathf.Max(Distance, bounds.size.x * .6f / (Mathf.Tan(15 * Mathf.Deg2Rad) * Mathf.Max(.1f, rect.width / rect.height)));
            renderedDistance = actualDistance;
            var rotation = Quaternion.Euler(Pitch, Yaw, 0);
            utility.camera.transform.position = Pivot + rotation * Vector3.forward * actualDistance;
            utility.camera.transform.rotation = Quaternion.LookRotation(Pivot - utility.camera.transform.position, rotation * Vector3.up);
            utility.camera.nearClipPlane = Mathf.Max(.001f, actualDistance / 1000);
            utility.camera.farClipPlane = Mathf.Max(actualDistance, Vector3.Distance(utility.camera.transform.position, bounds.center)) + bounds.size.magnitude * 3 + 1;
            utility.BeginPreview(new Rect(0, 0, rect.width, rect.height), GUIStyle.none);
            try { utility.Render(true); }
            finally { Frame = utility.EndPreview(); }
            LastSize = rect.size; Dirty = false; RenderCount++;
            RenderedControls = sampledControls?.DeepClone() as JObject;
            RenderedView = new JObject { ["yaw"] = Yaw, ["pitch"] = Pitch, ["distance"] = actualDistance,
                ["focus"] = FocusName, ["pivot"] = new JArray(Pivot.x, Pivot.y, Pivot.z), ["projection"] = "perspective",
                ["field_of_view"] = 30, ["captured_at"] = WorkbenchData.Now };
            return Frame;
        }
        public Texture2D Capture()
        {
            if (!(Frame is RenderTexture rt)) throw new InvalidOperationException("请等待真实模型画面显示后再指出问题。");
            var previous = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            RenderTexture encoded = null;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "AW_FrozenFrame" };
            try
            {
                // PreviewRenderUtility can return a linear framebuffer. Encode it once for
                // an ordinary sRGB PNG/Texture2D so the frozen view matches the live view.
                if (QualitySettings.activeColorSpace == ColorSpace.Linear && !rt.sRGB)
                {
                    encoded = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    GL.sRGBWrite = true;
                    Graphics.Blit(rt, encoded);
                }
                RenderTexture.active = encoded ? encoded : rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                return tex;
            }
            catch { Object.DestroyImmediate(tex); throw; }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgbWrite;
                if (encoded) RenderTexture.ReleaseTemporary(encoded);
            }
        }
        public void Dispose()
        {
            StopGesture(); Frame = null; RenderedView = null; RenderedControls = null; sampledControls = null;
            if (utility != null) { utility.Cleanup(); utility = null; }
            if (simulationRoot) Object.DestroyImmediate(simulationRoot); simulationRoot = null;
            if (simulationScene.IsValid()) EditorSceneManager.ClosePreviewScene(simulationScene); simulationScene = default;
            foreach (var material in temporaryMaterials) if (material) Object.DestroyImmediate(material); temporaryMaterials.Clear();
            clone = null; bones.Clear(); liveRenderers.Clear(); RendererCount = 0; VisibleRendererCount = 0; renderedDistance = 0; autoFrame = true;
        }
    }
}

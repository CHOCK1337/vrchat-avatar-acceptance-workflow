using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace AvatarWorkbench
{
    // Reads the installed NDMF session's existing output. Never builds or changes its controls.
    internal static class WorkbenchScenePreview
    {
        static Type sessionType;
        static PropertyInfo current, rendererMap;
        static bool discovered;

        public static Dictionary<Renderer, Renderer> Capture(GameObject root, out string notice)
        {
            notice = "";
            var result = new Dictionary<Renderer, Renderer>();
            if (!root || EditorUtility.IsPersistent(root)) return result;
            if (!discovered)
            {
                discovered = true;
                sessionType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("nadena.dev.ndmf.preview.PreviewSession")).FirstOrDefault(t => t != null);
                var flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                current = sessionType?.GetProperty("Current", flags);
                rendererMap = sessionType?.GetProperty("OriginalToProxyRenderer", flags);
            }
            if (current == null) return result;
            if (rendererMap == null) { notice = "本版 NDMF 不提供已识别的场景预览输出；显示原始网格。"; return result; }
            var session = current.GetValue(null);
            if (session == null) return result;
            if (rendererMap.GetValue(session) is IEnumerable map)
                foreach (var item in map)
                {
                    var type = item.GetType();
                    var source = type.GetProperty("Key")?.GetValue(item) as Renderer;
                    var proxy = type.GetProperty("Value")?.GetValue(item) as Renderer;
                    if (source && source.transform.IsChildOf(root.transform) && proxy) result[source] = proxy;
                }
            notice = result.Count > 0 ? "同一场景角色 · 已有 MA/NDMF 功能预览，非最终构建" : "同一场景角色 · NDMF 尚无可复用输出，显示原始网格";
            return result;
        }

        // Only the selected avatar's small existing proxy map is inspected by the local UI timer.
        // No AssetDatabase search, BakeMesh, capture, task write or model request.
        public static string Signature(GameObject root)
        {
            var map = Capture(root, out string notice);
            var s = new StringBuilder(notice);
            foreach (var pair in map.OrderBy(p => p.Key.GetInstanceID()))
            {
                var r = pair.Value;
                s.Append('|').Append(pair.Key.GetInstanceID()).Append(':').Append(r.GetInstanceID()).Append(':').Append(r.enabled).Append(':').Append(r.gameObject.activeInHierarchy);
                foreach (var m in r.sharedMaterials) s.Append(':').Append(m ? m.GetInstanceID() : 0);
                if (r is SkinnedMeshRenderer skin && skin.sharedMesh)
                {
                    s.Append(':').Append(skin.sharedMesh.GetInstanceID());
                    for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) s.Append(':').Append(skin.GetBlendShapeWeight(i).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            return s.ToString();
        }
    }

    public sealed partial class WorkbenchWindow
    {
        bool followScene = true, refreshPending, refreshingPreview, sceneBindingPending = true, preferActiveScene;
        GameObject pendingSelection;
        double nextLocalPollAt, sceneRefreshAt;
        int observedSelectionId;
        string scenePreviewSignature = "", sceneBindingNotice = "正在读取当前场景角色。";
        string sourceHierarchySignature = "";
        readonly HashSet<int> watchedObjects = new HashSet<int>();
        readonly HashSet<string> watchedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IVisualElementScheduledItem sceneRefreshJob;
        Label sceneSourceLabel;
        internal bool FollowsTarget(string globalId) => target && GlobalObjectId.GetGlobalObjectIdSlow(target).ToString() == globalId;

        void StartSceneSync()
        {
            StopSceneSync();
            observedSelectionId = Selection.activeInstanceID;
            nextLocalPollAt = EditorApplication.timeSinceStartup + .25;
            EditorApplication.update += SceneSyncEditorUpdate;
            Selection.selectionChanged += FollowSelection;
            ObjectChangeEvents.changesPublished += SourceObjectsChanged;
            EditorApplication.hierarchyChanged += SourceHierarchyChanged;
            Undo.undoRedoPerformed += SourceHierarchyChanged;
            EditorSceneManager.sceneOpened += SourceSceneOpened;
            EditorSceneManager.sceneClosed += SourceSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode += SourceActiveSceneChanged;
            PrefabStage.prefabStageOpened += SourcePrefabStageChanged;
            PrefabStage.prefabStageClosing += SourcePrefabStageChanged;
        }
        void StopSceneSync()
        {
            sceneRefreshJob?.Pause(); sceneRefreshJob = null;
            EditorApplication.update -= SceneSyncEditorUpdate;
            Selection.selectionChanged -= FollowSelection;
            ObjectChangeEvents.changesPublished -= SourceObjectsChanged;
            EditorApplication.hierarchyChanged -= SourceHierarchyChanged;
            Undo.undoRedoPerformed -= SourceHierarchyChanged;
            EditorSceneManager.sceneOpened -= SourceSceneOpened;
            EditorSceneManager.sceneClosed -= SourceSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode -= SourceActiveSceneChanged;
            PrefabStage.prefabStageOpened -= SourcePrefabStageChanged;
            PrefabStage.prefabStageClosing -= SourcePrefabStageChanged;
            watchedObjects.Clear(); watchedAssets.Clear(); sceneBindingPending = true;
        }
        static GameObject AvatarRoot(GameObject go)
        {
            if (!go) return null;
            for (var t = go.transform; t; t = t.parent)
                if (t.GetComponents<Component>().Any(c => c && c.GetType().Name == "VRCAvatarDescriptor")) return t.gameObject;
            return null;
        }
        static List<GameObject> SceneAvatars(Scene scene)
        {
            var result = new List<GameObject>();
            if (!scene.IsValid() || !scene.isLoaded) return result;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.GetComponents<Component>().Any(c => c && c.GetType().Name == "VRCAvatarDescriptor")) result.Add(t.gameObject);
            return result;
        }
        static bool IsEditingAvatar(GameObject go)
        {
            if (!go || EditorUtility.IsPersistent(go) || !go.scene.IsValid() || !go.scene.isLoaded || AvatarRoot(go) != go) return false;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return !EditorSceneManager.IsPreviewScene(go.scene) || stage != null && stage.scene == go.scene;
        }
        static bool IsPrefabEditingTarget(GameObject go)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return go && stage != null && go.scene == stage.scene;
        }
        static string AssetPathKey(string path) => (path ?? "").Replace('\\', '/');
        List<GameObject> LoadedSceneAvatars()
        {
            var avatars = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                avatars.AddRange(SceneAvatars(scene));
            }
            return avatars;
        }
        GameObject SceneTargetFor(GameObject candidate, List<GameObject> avatars = null)
        {
            if (IsEditingAvatar(candidate)) return candidate;
            string path = AssetPathKey(candidate ? AssetDatabase.GetAssetPath(candidate) : CandidatePath);
            string taskScene = AssetPathKey(WorkbenchData.Text(task["new_scene"]));
            var matches = (avatars ?? LoadedSceneAvatars()).Where(avatar =>
                !string.IsNullOrEmpty(path) && string.Equals(AssetPathKey(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatar)), path, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(path) && string.Equals(path, AssetPathKey(CandidatePath), StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(taskScene) && string.Equals(AssetPathKey(avatar.scene.path), taskScene, StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        GameObject ResolveSceneTarget(out string notice)
        {
            notice = "";
            var selected = AvatarRoot(pendingSelection ? pendingSelection : Selection.activeGameObject);
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                var stageAvatars = SceneAvatars(stage.scene);
                if (IsEditingAvatar(selected) && selected.scene == stage.scene) return selected;
                if (stageAvatars.Count == 1) return stageAvatars[0];
                if (stageAvatars.Count > 1)
                {
                    if (IsEditingAvatar(target) && target.scene == stage.scene) return target;
                    notice = "Prefab 编辑模式有多个角色，请在 Hierarchy 点选要修改的角色。"; return null;
                }
            }
            if (IsEditingAvatar(selected)) return selected;
            var avatars = LoadedSceneAvatars();
            var active = avatars.Where(a => a.scene == SceneManager.GetActiveScene()).ToList();
            if (preferActiveScene && active.Count == 1) return active[0];
            if (IsEditingAvatar(target)) return target;
            var linked = SceneTargetFor(target, avatars);
            if (linked) return linked;
            if (active.Count == 1) return active[0];
            if (avatars.Count == 1) return avatars[0];
            if (avatars.Count > 1) { notice = "场景中有多个角色，请在 Hierarchy 点选要修改的角色。"; return null; }
            var candidate = target && EditorUtility.IsPersistent(target) ? target : AssetDatabase.LoadAssetAtPath<GameObject>(CandidatePath);
            if (!candidate && selected && EditorUtility.IsPersistent(selected)) candidate = selected;
            if (candidate && AvatarRoot(candidate) == candidate)
            { notice = "场景中暂无角色，显示已有磁盘候选；未运行 MA/NDMF。"; return candidate; }
            notice = "当前场景没有 Avatar；角色加入场景后会自动显示。";
            return null;
        }
        void BindSceneIfAvailable()
        {
            if (!followScene) { sceneBindingPending = false; pendingSelection = null; return; }
            if (IsBusy()) return;
            target = ResolveSceneTarget(out sceneBindingNotice);
            pendingSelection = null; preferActiveScene = false; sceneBindingPending = false;
            sourceKind = target && EditorUtility.IsPersistent(target) ? "candidate_prefab" : IsPrefabEditingTarget(target) ? "prefab_stage" : "editing_state";
            targetField?.SetValueWithoutNotify(target);
            if (!target) sourceNotice = sceneBindingNotice;
        }
        void UseSceneTarget()
        {
            if (IsBusy()) { Toast("Codex 正在修改现场，先保留当前画面。"); return; }
            followScene = true; sceneBindingPending = true; BindSceneIfAvailable(); ClearHistory();
            LoadPreview(); SaveState(); UpdateLabels();
            if (!target) Toast(sceneBindingNotice);
        }
        void FollowSelection()
        {
            if (!followScene || disposed) return;
            observedSelectionId = Selection.activeInstanceID;
            var go = Selection.activeGameObject;
            // Project resource cards never switch the avatar. Remember a scene selection even
            // while the consumer is busy; resolve it only after the writing operation ends.
            if (!go || EditorUtility.IsPersistent(go)) return;
            pendingSelection = go; sceneBindingPending = true; QueueSceneRefresh();
        }
        void SourceSceneOpened(Scene scene, OpenSceneMode mode) { SourceSceneContextChanged(); }
        void SourceSceneClosed(Scene scene) { SourceSceneContextChanged(); }
        void SourceActiveSceneChanged(Scene beforeScene, Scene afterScene) { SourceSceneContextChanged(); }
        void SourcePrefabStageChanged(PrefabStage stage) { SourceSceneContextChanged(); }
        void SourceSceneContextChanged()
        {
            if (!followScene || disposed) return;
            preferActiveScene = true; sceneBindingPending = true; QueueSceneRefresh();
        }
        void SourceHierarchyChanged()
        {
            if (disposed || refreshingPreview) return;
            // Temporary preview objects also raise hierarchyChanged. They must not reset
            // an active try-on session. Inspect only this selected avatar on that event.
            string signature = SourceHierarchySignature();
            if (target && signature == sourceHierarchySignature) return;
            sourceHierarchySignature = signature;
            if (followScene) sceneBindingPending = true;
            QueueSceneRefresh();
        }
        string SourceHierarchySignature() => !target ? "" : string.Join(",", target.GetComponentsInChildren<Transform>(true).Select(t => t.GetInstanceID() + ":" + (t.parent ? t.parent.GetInstanceID() : 0) + ":" + t.gameObject.activeSelf + ":" + t.name));
        internal void SourceAssetsChanged(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (disposed || refreshingPreview) return;
            if (new[] { imported, deleted, moved, movedFrom }.Any(paths => paths.Any(p => watchedAssets.Contains(AssetPathKey(p))))) QueueSceneRefresh();
        }
        void SourceObjectsChanged(ref ObjectChangeEventStream stream)
        {
            if (disposed || refreshingPreview || !target) return;
            for (int i = 0; i < stream.length; i++)
            {
                int id = 0;
                switch (stream.GetEventType(i))
                {
                    case ObjectChangeKind.ChangeGameObjectOrComponentProperties:
                        stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var component); id = component.instanceId; break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(i, out var asset); id = asset.instanceId; break;
                }
                if (watchedObjects.Contains(id)) { QueueSceneRefresh(); break; }
            }
        }
        void WatchPreviewSource()
        {
            watchedObjects.Clear(); watchedAssets.Clear();
            if (!target) return;
            WatchAsset(target); WatchAssetPath(target.scene.path);
            foreach (var t in target.GetComponentsInChildren<Transform>(true))
            {
                watchedObjects.Add(t.gameObject.GetInstanceID());
                foreach (var c in t.GetComponents<Component>()) if (c) watchedObjects.Add(c.GetInstanceID());
                var r = t.GetComponent<Renderer>();
                if (r)
                {
                    if (r is SkinnedMeshRenderer skin) WatchAsset(skin.sharedMesh);
                    else { var filter = t.GetComponent<MeshFilter>(); if (filter) WatchAsset(filter.sharedMesh); }
                    foreach (var m in r.sharedMaterials) if (m) { watchedObjects.Add(m.GetInstanceID()); WatchAsset(m); }
                }
            }
            scenePreviewSignature = WorkbenchScenePreview.Signature(target);
            sourceHierarchySignature = SourceHierarchySignature();
        }
        void WatchAsset(UnityEngine.Object asset) { if (asset) WatchAssetPath(AssetDatabase.GetAssetPath(asset)); }
        void WatchAssetPath(string path)
        {
            path = AssetPathKey(path);
            if (string.IsNullOrEmpty(path) || !watchedAssets.Add(path)) return;
            // Cache only the selected source's referenced assets; never search the project.
            if (path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                foreach (var dependency in AssetDatabase.GetDependencies(path, false)) watchedAssets.Add(AssetPathKey(dependency));
        }
        internal void QueueSceneRefresh()
        {
            refreshPending = true;
            sceneRefreshAt = EditorApplication.timeSinceStartup + .2;
        }
        void SceneSyncEditorUpdate()
        {
            if (disposed || refreshingPreview || !windowVisible || rootVisualElement.panel == null) return;
            double now = EditorApplication.timeSinceStartup;
            TryAction(() => TickPreviewControls(now));
            // UI Toolkit timers can stop with an inactive tab or after a layout restoration.
            // A standard Editor update owns this small local timer; idle frames only check time.
            if (now >= nextLocalPollAt) { nextLocalPollAt = now + 2; TryAction(PollLocalReceipts); }
            if (!refreshPending || now < sceneRefreshAt) return;
            if (IsBusy()) { sceneRefreshAt = now + 2; return; }
            refreshPending = false;
            TryAction(() => { if (sceneBindingPending) BindSceneIfAvailable(); LoadPreview(); SaveState(); UpdateLabels(); });
        }
        void PollSceneSync()
        {
            // Some Editor automation batches delay selectionChanged. Read only the selected ID
            // on the existing two-second local receipt tick, without scanning any source objects.
            if (followScene && Selection.activeInstanceID != observedSelectionId) FollowSelection();
            if (IsBusy()) { refreshPending = true; return; }
            if (sceneBindingPending || refreshPending) { if (!refreshPending) QueueSceneRefresh(); return; }
            if (target && !EditorUtility.IsPersistent(target))
            {
                string next = WorkbenchScenePreview.Signature(target);
                if (next != scenePreviewSignature) QueueSceneRefresh();
            }
        }
    }

    internal sealed class WorkbenchSourceAssetChanges : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<WorkbenchWindow>())
                window.SourceAssetsChanged(imported, deleted, moved, movedFrom);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    /// <summary>
    /// Read-only hand-in check a host runs before replying "addressed". It looks for the faults the
    /// 2026-10-31 field tests left behind: extra active avatars, unsaved work, empty Modular Avatar
    /// references, outfit meshes fitted for another body and body masks that stay on after a wardrobe
    /// change hides their garment (WorkbenchFinishCheck.Masks.cs). The receipt is written next to the inbox
    /// so the reply gate and the workbench can show it; nothing in the project is changed.
    /// </summary>
    public static partial class WorkbenchFinishCheck
    {
        const string GuideGuid = "6332601b92834bddb4214cbab3e00536";
        // The call template ships with the workbench itself, so it exists even when the installer reused an
        // older Skill that predates it.
        internal static string GuidePath
        {
            get
            {
                string asset = AssetDatabase.GUIDToAssetPath(GuideGuid);
                return Path.GetFullPath(Path.Combine(WorkbenchData.Project, string.IsNullOrEmpty(asset) ? "Assets/AvatarWorkbench/Tools/finish-check.md" : asset));
            }
        }
        internal static readonly string[] KnownBases = { "Eku", "Milfy", "Shinano", "Manuka", "Kikyo", "Selestia", "Karin", "Chiffon", "Chocolat",
            "Lime", "Airi", "Mizuki", "Rusk", "Moe", "Sio", "Milltina", "Rurune", "Mafuyu", "Ririka", "Shiratsume", "Mamehinata", "Kaguya" };
        const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <param name="json">{"project": exact path, "candidate_prefab"?: "Assets/...prefab", "base"?: "Eku", "binding"?: task or selection path}</param>
        public static string Run(string json)
        {
            var request = string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json);
            string project = WorkbenchData.Text(request["project"]);
            if (!string.IsNullOrEmpty(project) && !string.Equals(Path.GetFullPath(project).TrimEnd('\\', '/'), Path.GetFullPath(WorkbenchData.Project).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PROJECT_MISMATCH: this Unity instance is " + WorkbenchData.Project);
            var errors = new JArray(); var warnings = new JArray();
            void Add(JArray list, string code, string path, string detail) => list.Add(new JObject { ["code"] = code, ["path"] = path, ["detail"] = detail });

            var avatars = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                var found = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                    .Where(t => t.gameObject.activeInHierarchy && !t.CompareTag("EditorOnly") && HasComponent(t.gameObject, "VRCAvatarDescriptor"))
                    .Select(t => t.gameObject).ToList();
                avatars.AddRange(found);
                if (found.Count > 0 && (scene.isDirty || string.IsNullOrEmpty(scene.path)))
                    Add(errors, "UNSAVED_SCENE", scene.path, "场景里有角色但没有保存；关掉 Unity 后这次修改会丢失。");
                else if (scene.isDirty) Add(warnings, "UNSAVED_SCENE", scene.path, "场景有未保存的改动。");
            }
            if (avatars.Count > 1)
                Add(errors, "MULTIPLE_ACTIVE_AVATARS", string.Join(", ", avatars.Select(a => a.name)), "场景里同时有多个激活的角色；测试用副本请删除、停用或标成 EditorOnly，否则上传时会多出角色。");

            string candidatePath = WorkbenchData.Text(request["candidate_prefab"]);
            GameObject loaded = null;
            try
            {
                if (!string.IsNullOrEmpty(candidatePath))
                {
                    if (!File.Exists(Path.Combine(WorkbenchData.Project, candidatePath))) Add(errors, "CANDIDATE_MISSING", candidatePath, "回复里提到的修改版本不存在。");
                    else loaded = PrefabUtility.LoadPrefabContents(candidatePath);
                }
                var roots = avatars.Concat(loaded ? new[] { loaded } : Array.Empty<GameObject>()).ToList();
                if (roots.Count == 0) Add(errors, "NO_AVATAR", "", "场景和指定的修改版本里都没有找到角色。");
                string targetBase = WorkbenchData.Text(request["base"]);
                foreach (var root in roots)
                {
                    string label = loaded == root ? candidatePath : root.name;
                    if (string.IsNullOrEmpty(targetBase)) targetBase = InferBase(root);
                    CheckAvatar(root, label, targetBase, errors, warnings);
                }
                var receipt = new JObject { ["format"] = "avatar-workbench-finish-check-v1", ["checked_at"] = WorkbenchData.Now,
                    ["project"] = WorkbenchData.Project, ["candidate_prefab"] = candidatePath, ["base"] = targetBase,
                    ["avatars"] = new JArray(roots.Select(r => loaded == r ? candidatePath : r.name)),
                    ["passed"] = errors.Count == 0, ["errors"] = errors, ["warnings"] = warnings };
                string binding = WorkbenchData.Text(request["binding"]);
                if (string.IsNullOrEmpty(binding))
                {
                    var window = Resources.FindObjectsOfTypeAll<WorkbenchWindow>().FirstOrDefault();
                    binding = window != null ? window.BindingPath : WorkbenchData.SelectionBinding;
                }
                WorkbenchData.Write(Path.Combine(WorkbenchData.Space(binding), "finish-check.json"), receipt);
                return receipt.ToString();
            }
            finally { if (loaded) PrefabUtility.UnloadPrefabContents(loaded); }
        }

        static void CheckAvatar(GameObject root, string label, string targetBase, JArray errors, JArray warnings)
        {
            string Where(Component c) => label + "/" + AnimationUtility.CalculateTransformPath(c.transform, root.transform);
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (missing > 0) errors.Add(Issue("MISSING_SCRIPT", label + "/" + AnimationUtility.CalculateTransformPath(t, root.transform), missing + " 个组件脚本丢失。"));
            }
            foreach (var c in root.GetComponentsInChildren<Component>(true).Where(c => c))
            {
                string type = c.GetType().Name;
                if (type == "ModularAvatarMergeAnimator" && !(Field(c, "animator") as Object))
                    errors.Add(Issue("MA_MERGE_ANIMATOR_EMPTY", Where(c), "Merge Animator 没有指定动画控制器（NDMF MA-1300），这个功能不会生效。"));
                else if (type == "ModularAvatarObjectToggle" && Field(c, "Objects") is System.Collections.IEnumerable toggled)
                {
                    int empty = toggled.Cast<object>().Count(o => string.IsNullOrEmpty(Field(Field(o, "Object"), "referencePath") as string));
                    if (empty > 0) errors.Add(Issue("MA_TOGGLE_TARGET_EMPTY", Where(c), empty + " 个开关目标是空的。"));
                }
                else if (type == "ModularAvatarMenuItem" && c.gameObject.activeInHierarchy)
                {
                    var control = Field(c, "Control");
                    bool submenu = Convert.ToString(Field(control, "type")) == "SubMenu";
                    bool children = Convert.ToString(Field(c, "MenuSource")) == "Children";
                    if (submenu && children && !c.transform.Cast<Transform>().Any(child => child.gameObject.activeSelf))
                        warnings.Add(Issue("EMPTY_SUBMENU", Where(c), "子菜单下面没有任何项目。"));
                }
            }
            CheckBodyMasks(root, label, errors, warnings);
            if (string.IsNullOrEmpty(targetBase)) return;
            // Outfit meshes imported from another body's package keep that body's name in their asset path.
            // One warning per source model: an outfit has many renderers, and the host only needs the outfit.
            var foreign = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh)
                .Select(s => new { skin = s, path = AssetDatabase.GetAssetPath(s.sharedMesh) })
                .Where(x => !string.IsNullOrEmpty(x.path) && !Mentions(x.path, targetBase))
                .Select(x => new { x.skin, x.path, other = KnownBases.FirstOrDefault(b => !b.Equals(targetBase, StringComparison.OrdinalIgnoreCase) && Mentions(x.path, b)) })
                .Where(x => x.other != null).GroupBy(x => x.path);
            foreach (var model in foreign)
            {
                var first = model.First();
                var outfit = first.skin.transform; while (outfit.parent && outfit.parent != root.transform) outfit = outfit.parent;
                warnings.Add(Issue("OTHER_BODY_MESH", label + "/" + outfit.name, model.Count() + " 个网格来自 " + first.other + " 体型的素材（" + model.Key + "），穿在 " + targetBase + " 上可能不贴合或有色差，请确认。"));
            }
        }

        internal static string InferBase(GameObject root)
        {
            var descriptor = root.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "VRCAvatarDescriptor");
            var body = Field(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            string source = (body && body.sharedMesh ? AssetDatabase.GetAssetPath(body.sharedMesh) : "") + " " + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) + " " + root.name;
            return KnownBases.FirstOrDefault(b => Mentions(source, b)) ?? "";
        }
        // Whole-word match, so "Sio" does not hit "Version" and "Lime" does not hit "Sublime".
        internal static bool Mentions(string text, string name) =>
            Regex.IsMatch(text, "(?<![A-Za-z])" + Regex.Escape(name) + "(?![A-Za-z])", RegexOptions.IgnoreCase);
        static bool HasComponent(GameObject go, string typeName) => go.GetComponents<Component>().Any(c => c && c.GetType().Name == typeName);
        static object Field(object target, string name)
        {
            if (target == null) return null;
            var type = target.GetType();
            return type.GetField(name, Fields)?.GetValue(target) ?? type.GetProperty(name, Fields)?.GetValue(target);
        }
        static JObject Issue(string code, string path, string detail) => new JObject { ["code"] = code, ["path"] = path, ["detail"] = detail };
    }
}

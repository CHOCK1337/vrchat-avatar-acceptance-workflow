using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    internal sealed class WorkbenchMenuControl
    {
        public string Name, Parameter, Type, Path;
        public float Value;
        public readonly List<KeyValuePair<string, float>> Parents = new List<KeyValuePair<string, float>>();
    }

    // An optional adapter to the installed Gesture Manager 3.9 API. No asset generation,
    // build pipeline, upload, SetActive wardrobe emulation or separate Animator simulator.
    internal sealed class WorkbenchGesturePreview : IDisposable
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string ModuleName = "BlackStartX.GestureManager.Editor.Modules.Vrc3.ModuleVrc3";
        internal static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        internal static object Member(object value, string name) => value == null ? null : value.GetType().GetField(name, Flags)?.GetValue(value) ?? value.GetType().GetProperty(name, Flags)?.GetValue(value);
        internal static string Plain(string text) => Regex.Replace(text ?? "", "<[^>]*>", "").Trim();
        static Component Descriptor(GameObject source) => source ? source.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") : null;

        public static List<WorkbenchMenuControl> ReadMenu(GameObject source)
        {
            var controls = new List<WorkbenchMenuControl>();
            var visited = new HashSet<Object>();
            Action<object, string, List<KeyValuePair<string, float>>, int> read = null;
            read = (menu, path, parents, depth) =>
            {
                if (!(menu is Object asset) || !asset || depth > 12 || !visited.Add(asset) || controls.Count >= 400) return;
                if (!(Member(menu, "controls") is IEnumerable items)) return;
                foreach (var item in items)
                {
                    string name = Plain(Member(item, "name") as string);
                    string kind = Member(item, "type")?.ToString();
                    string parameter = Member(Member(item, "parameter"), "name") as string ?? "";
                    float value = Convert.ToSingle(Member(item, "value") ?? 0f);
                    var control = new WorkbenchMenuControl { Name = name, Path = path, Type = kind, Parameter = parameter, Value = value };
                    control.Parents.AddRange(parents); controls.Add(control);
                    var nextParents = new List<KeyValuePair<string, float>>(parents);
                    if (kind == "SubMenu" && !string.IsNullOrEmpty(parameter)) nextParents.Add(new KeyValuePair<string, float>(parameter, value));
                    read(Member(item, "subMenu"), path + "/" + name, nextParents, depth + 1);
                }
            };
            read(Member(Descriptor(source), "expressionsMenu"), "", new List<KeyValuePair<string, float>>(), 0);
            return controls;
        }

        public static List<WorkbenchMenuControl> Outfits(List<WorkbenchMenuControl> controls)
        {
            // Only an existing wardrobe group with one shared selector is offered as outfits.
            // A random body/face toggle or a shoe component is not an outfit.
            var groups = controls.Where(c => c.Type == "Toggle" && !string.IsNullOrEmpty(c.Parameter))
                .GroupBy(c => c.Parameter)
                .Where(g => g.Select(c => c.Value).Distinct().Count() >= 2 && g.Any(c => Regex.IsMatch(c.Path + "/" + c.Parameter, "衣服|服装|穿搭|衣装|コーデ|outfit|wardrobe|clothes", RegexOptions.IgnoreCase)))
                .OrderByDescending(g => g.Count()).ToList();
            return groups.Count == 0 ? new List<WorkbenchMenuControl>() : groups[0].GroupBy(c => c.Value).Select(g => g.First()).Take(32).ToList();
        }

        public static string Availability(GameObject source)
        {
            if (!source) return "先选择角色。";
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "请先退出 Unity 播放模式，再使用独立预览。";
            if (FindType(ModuleName) == null) return "当前工程未安装兼容的 Gesture Manager；保留普通模型预览。";
            var descriptor = Descriptor(source);
            if (!descriptor || !Member(descriptor, "expressionsMenu").IsUnityObject()) return "角色缺少可读取的 VRChat 菜单。";
            var animator = source.GetComponent<Animator>();
            if (!animator || !animator.avatar || !animator.avatar.isHuman) return "角色缺少有效的人形 Animator。";
            if (source.GetComponentsInChildren<Component>(true).Any(c => c && c.GetType().Namespace != null && c.GetType().Namespace.StartsWith("nadena.dev.modular_avatar", StringComparison.Ordinal)))
                return "这个角色还有待处理的 MA 组件；请选择流程已经保存的处理后候选。";
            var module = FindType(ModuleName);
            if (module.GetField("_playableGraph", Flags) == null || module.GetField("_memoryClone", Flags) == null || module.GetMethod("Connect") == null || module.GetMethod("Disconnect") == null)
                return "本版 Gesture Manager 的接口不兼容；保留普通模型预览。";
            return "";
        }

        object module;
        Type moduleType;
        IDictionary parameters;
        PlayableGraph graph;
        readonly List<GameObject> ownedManagers = new List<GameObject>();
        GameObject memory;
        GameObject previewRoot;
        WorkbenchPhysicsPreview physics;
        Vector3 rootPosition;
        Quaternion rootRotation;
        float shakeRemaining, shakeElapsed;
        double lastTick;
        float remaining, elapsed, pulseRemaining;
        string pulseParameter;
        float pulseValue;
        WorkbenchMenuControl reset, sit;
        List<WorkbenchMenuControl> outfits;
        public string OutfitName { get; private set; } = "原菜单默认";
        public string PoseName { get; private set; } = "站立";
        public bool Moving => remaining > 0;
        public bool Connected => module != null && graph.IsValid();
        public bool PhysicsRunning => physics?.Running == true;
        public bool PhysicsSimulated => physics?.Simulated == true;
        public string PhysicsError => physics?.Error;
        // Exposed only to the Editor radial adapter. The module continues to be owned
        // and disposed here, and always targets the isolated preview clone.
        internal object NativeModule => Connected ? module : null;

        public void Connect(GameObject clone, Scene previewScene, List<WorkbenchMenuControl> menu)
        {
            previewRoot = clone; rootPosition = clone.transform.position; rootRotation = clone.transform.rotation;
            moduleType = FindType(ModuleName);
            try
            {
                // GM's automatic manager restart recreates scene components. Initialize only
                // missing SDK managers inside our preview scene so that restart is never used.
                foreach (string name in new[] { "VRC.Dynamics.ContactManager", "VRC.Dynamics.PhysBoneManager" })
                {
                    var type = FindType(name) ?? throw new InvalidOperationException("VRChat 动态组件接口不可用。");
                    var instance = type.GetField("Inst", Flags);
                    if (instance == null) throw new InvalidOperationException("VRChat 动态组件版本不兼容。");
                    if (instance.GetValue(null) is Object current && current) continue;
                    var go = new GameObject("AW_" + type.Name) { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(go, previewScene); ownedManagers.Add(go);
                    var component = go.AddComponent(type);
                    type.GetMethod("Awake", Flags)?.Invoke(component, null);
                    if (!(instance.GetValue(null) is Object initialized) || !initialized) throw new InvalidOperationException("临时动态组件未初始化，已停止预览。");
                    type.GetField("IsSDK", Flags)?.SetValue(component, true);
                    type.GetMethod("Init", Flags)?.Invoke(component, null);
                }
                var descriptor = Descriptor(clone);
                module = Activator.CreateInstance(moduleType, new object[] { descriptor, null });
                // Give the native module its normal rollback clone in the same temporary scene.
                memory = Object.Instantiate(clone); memory.name = "AW_GestureMemory";
                SceneManager.MoveGameObjectToScene(memory, previewScene); HideTree(memory); memory.SetActive(false);
                moduleType.GetField("_memoryClone", Flags).SetValue(module, Descriptor(memory));
                var settings = Activator.CreateInstance(FindType("BlackStartX.GestureManager.Modules.ModuleSettings"));
                Invoke("Connect", settings);
                graph = (PlayableGraph)moduleType.GetField("_playableGraph", Flags).GetValue(module);
                if (!graph.IsValid()) throw new InvalidOperationException("Gesture Manager 没有生成有效的动画预览。");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                parameters = (IDictionary)moduleType.GetField("Params").GetValue(module);
                outfits = Outfits(menu);
                reset = menu.FirstOrDefault(c => c.Path.IndexOf("GoGo", StringComparison.OrdinalIgnoreCase) >= 0 && c.Type == "Button" && c.Parameter == "VRCEmote" && c.Value == 255);
                sit = menu.FirstOrDefault(c => c.Path.IndexOf("GoGo", StringComparison.OrdinalIgnoreCase) >= 0 && c.Type == "Button" && c.Parameter == "VRCEmote" && c.Value == 252);
                // Evaluate the existing native graph once, then stop. No NDMF or build call.
                for (int i = 0; i < 18; i++) Evaluate(1f / 30f);
                SyncOutfitName();
                graph.Stop(); lastTick = EditorApplication.timeSinceStartup;
            }
            catch (Exception e) { Dispose(); throw new InvalidOperationException("原生功能预览未启动：" + (e.InnerException ?? e).Message); }
        }

        public static void HideTree(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                foreach (var component in transform.GetComponents<Component>()) if (component) component.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        void Invoke(string name, params object[] args) => moduleType.GetMethod(name).Invoke(module, args);
        object Param(string name) => parameters != null && parameters.Contains(name) ? parameters[name] : null;
        void SetParam(string name, float value)
        {
            var parameter = Param(name) ?? throw new InvalidOperationException("处理后动画没有菜单参数：" + name);
            parameter.GetType().GetMethod("Set", new[] { moduleType, typeof(float), typeof(object) }).Invoke(parameter, new object[] { module, value, null });
        }
        float ReadParam(object parameter) => Convert.ToSingle(parameter.GetType().GetMethod("FloatValue").Invoke(parameter, null));
        void SetNativePose(string name, bool enabled)
        {
            var parameter = moduleType.GetField(name, Flags).GetValue(module);
            parameter.GetType().GetMethod("Set", new[] { moduleType, typeof(float), typeof(object) }).Invoke(parameter, new object[] { module, enabled ? 1f : 0f, null });
        }
        void Apply(WorkbenchMenuControl control)
        {
            foreach (var pair in control.Parents) SetParam(pair.Key, pair.Value);
            // Selection of an exclusive outfit is idempotent; clicking the active outfit
            // does not toggle it off and expose the avatar.
            SetParam(control.Parameter, control.Value);
            if (control.Type == "Button") { pulseParameter = control.Parameter; pulseValue = control.Value; pulseRemaining = .2f; }
        }
        void Wake(float seconds) { remaining = seconds; lastTick = EditorApplication.timeSinceStartup; graph.Play(); }

        // These snapshots are taken only around real radial input or its one-time
        // release. Repaint/Layout never enumerate parameters or restart the graph.
        internal Dictionary<string, float> CaptureNativeMenuParameters()
        {
            var values = new Dictionary<string, float>();
            if (parameters != null)
                foreach (DictionaryEntry pair in parameters) values[(string)pair.Key] = ReadParam(pair.Value);
            return values;
        }
        internal bool CompleteNativeMenuInput(Dictionary<string, float> before)
        {
            if (!Connected || before == null || parameters == null) return false;
            bool changed = before.Count != parameters.Count;
            foreach (DictionaryEntry pair in parameters)
                if (!before.TryGetValue((string)pair.Key, out float previous) || !Mathf.Approximately(previous, ReadParam(pair.Value))) { changed = true; break; }
            if (!changed) return false;
            SyncOutfitName();
            PoseName = "原菜单操作";
            Wake(.65f);
            return true;
        }
        void SyncOutfitName()
        {
            if (outfits == null || outfits.Count == 0) return;
            var parameter = Param(outfits[0].Parameter);
            if (parameter == null) return;
            float value = ReadParam(parameter);
            var selected = outfits.FirstOrDefault(control => Mathf.Approximately(control.Value, value));
            OutfitName = selected?.Name ?? "原菜单当前组合";
        }

        public void SelectOutfit(WorkbenchMenuControl control)
        {
            Apply(control); OutfitName = control.Name; Wake(.65f);
        }
        public bool CanSit => sit != null;
        public void SelectPose(string pose)
        {
            if (pose == "sit" && sit == null) throw new InvalidOperationException("此角色没有已识别的 GoGoLoco 坐姿菜单。");
            SetNativePose("PoseT", false); SetNativePose("PoseIK", false);
            if (reset != null)
            {
                Apply(reset);
                // Let the author's reset parameter driver finish before choosing a new pose.
                for (int i = 0; i < 12; i++) Evaluate(1f / 30f);
                if (ReadParam(Param(reset.Parameter)) == reset.Value) SetParam(reset.Parameter, 0);
                pulseParameter = null;
            }
            if (pose == "t") SetNativePose("PoseT", true);
            else if (pose == "ik") SetNativePose("PoseIK", true);
            else if (pose == "sit") Apply(sit);
            PoseName = pose == "t" ? "T 姿" : pose == "ik" ? "伸臂（IK）" : pose == "sit" ? "坐姿" : "站立";
            Wake(1.1f);
        }
        void Evaluate(float delta)
        {
            graph.Evaluate(delta); Invoke("Update"); Invoke("LateUpdate"); elapsed += delta;
            SyncOutfitName();
            if (pulseParameter != null && (pulseRemaining -= delta) <= 0)
            {
                var param = Param(pulseParameter);
                if (param != null && Mathf.Approximately(ReadParam(param), pulseValue)) SetParam(pulseParameter, 0);
                pulseParameter = null;
            }
        }
        public void SetPhysics(bool running)
        {
            if (running && physics == null)
            {
                var manager = ownedManagers.Where(go => go).Select(go => go.GetComponent(FindType("VRC.Dynamics.PhysBoneManager"))).FirstOrDefault(c => c);
                if (!manager) throw new InvalidOperationException("其他 Unity 预览正在使用 PhysBone 管理器。请先结束那个预览，再开启本工作台的物理预览。");
                physics = new WorkbenchPhysicsPreview(previewRoot, manager);
            }
            physics?.SetRunning(running);
            if (running && !string.IsNullOrEmpty(physics?.Error)) throw new InvalidOperationException(physics.Error);
            lastTick = EditorApplication.timeSinceStartup;
            if (running) graph.Play();
            else if (!Moving) graph.Stop(); // Keep the exact paused root/PhysBone pose.
        }
        public void Shake()
        {
            SetPhysics(true); shakeElapsed = 0; shakeRemaining = 2f;
        }
        void RestoreRoot()
        {
            if (!previewRoot) return;
            previewRoot.transform.SetPositionAndRotation(rootPosition, rootRotation);
        }
        public bool Tick(bool visible)
        {
            double now = EditorApplication.timeSinceStartup;
            if (!visible || !Connected || (remaining <= 0 && !PhysicsRunning)) { lastTick = now; return false; }
            if (now - lastTick < 1.0 / 30) return false;
            float delta = Mathf.Min(.05f, (float)(now - lastTick)); lastTick = now;
            Evaluate(delta); remaining = Mathf.Max(0, remaining - delta);
            if (PhysicsRunning)
            {
                if (shakeRemaining > 0)
                {
                    shakeElapsed += delta; shakeRemaining = Mathf.Max(0, shakeRemaining - delta);
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(shakeElapsed / 2f));
                    float motion = Mathf.Sin(shakeElapsed * 9f) * envelope;
                    previewRoot.transform.SetPositionAndRotation(rootPosition + Vector3.right * (motion * .08f), rootRotation * Quaternion.Euler(0, motion * 12f, 0));
                }
                else RestoreRoot();
                if (!physics.Step(delta) && !string.IsNullOrEmpty(physics.Error)) throw new InvalidOperationException(physics.Error);
            }
            if (remaining <= 0 && !PhysicsRunning) graph.Stop();
            return true;
        }
        public JObject State()
        {
            var values = new JObject();
            if (parameters != null) foreach (DictionaryEntry pair in parameters) values[(string)pair.Key] = ReadParam(pair.Value);
            var state = new JObject { ["status"] = "known", ["preview_output"] = "gesture_manager_native_animation", ["scope"] = "temporary_preview_only",
                ["outfit"] = OutfitName, ["pose"] = PoseName, ["settled"] = !Moving, ["animation_sample_time"] = elapsed,
                ["parameters"] = values, ["t_pose"] = ReadParam(moduleType.GetField("PoseT", Flags).GetValue(module)) != 0,
                ["ik_pose"] = ReadParam(moduleType.GetField("PoseIK", Flags).GetValue(module)) != 0,
                ["physics_simulated"] = false, ["physics_running"] = false,
                ["note"] = "使用现有 Gesture Manager 动画图与 Unity SDK 原生 PhysBone；临时预览不写回角色，非最终构建。" };
            if (physics != null) foreach (var pair in physics.State()) state[pair.Key] = pair.Value.DeepClone();
            state["physics_stimulus"] = shakeRemaining > 0 ? "temporary_root_sway" : "none";
            return state;
        }
        public void Dispose()
        {
            physics?.Dispose(); physics = null; shakeRemaining = 0; RestoreRoot();
            try { if (module != null) Invoke("Disconnect"); }
            catch (Exception e) { Debug.LogWarning("Avatar Workbench：原生预览断开时 " + (e.InnerException ?? e).Message); }
            finally
            {
                if (graph.IsValid()) graph.Destroy(); module = null; parameters = null; remaining = 0;
                outfits = null;
                if (memory) Object.DestroyImmediate(memory); memory = null;
                // Ordinary SDK MonoBehaviours do not reliably receive OnDestroy in
                // Edit Mode. Release their native arrays explicitly before reload.
                foreach (var go in ownedManagers)
                {
                    if (!go) continue;
                    foreach (var component in go.GetComponents<Component>())
                    {
                        if (!component || component is Transform) continue;
                        var type = component.GetType();
                        try { type.GetMethod("Dispose", Flags, null, Type.EmptyTypes, null)?.Invoke(component, null); }
                        catch (Exception e) { Debug.LogWarning("Avatar Workbench：临时物理资源释放失败：" + (e.InnerException ?? e).Message); }
                        finally
                        {
                            var singleton = type.GetField("Inst", Flags);
                            if (singleton != null && ReferenceEquals(singleton.GetValue(null), component)) singleton.SetValue(null, null);
                        }
                    }
                    Object.DestroyImmediate(go);
                }
                ownedManagers.Clear();
                previewRoot = null;
            }
        }
    }

    internal static class WorkbenchUnityObjectExtensions
    {
        internal static bool IsUnityObject(this object value) => value is Object asset && asset;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace AvatarWorkbench
{
    /// <summary>
    /// Body masks that outlive their garment. Avatars hide skin under clothing with shape keys on the body
    /// (Eku keeps Lower_leg, Foot and Toe at 100 under its knee socks). In the 2026-10-31 run a new
    /// wardrobe entry hid the original socks and left those keys on, so the new outfit had no calves and
    /// the static checks passed. Garments and their masks are paired from the author's own toggle clips
    /// (garment off and key back to 0 in one clip) and from MA Shape Changers. Each menu state is then
    /// evaluated statically: parameter defaults plus that selection, the FX layers it settles into and the
    /// MA reactive components it enables. Nothing is played or changed.
    /// </summary>
    public static partial class WorkbenchFinishCheck
    {
        const int MaxMaskStates = 240, MaxMaskReports = 8;

        sealed class MaskLink { public string Owner, Key; public Component Changer; public float On, Off; }
        sealed class MaskItem { public Transform Transform; public string Parameter, Label; public float Value; public bool Default, Auto; }
        // A garment is hidden either by deactivating its object or by disabling its renderer.
        sealed class MaskClip
        {
            public readonly List<(string Path, bool Value, bool Renderer)> Active = new List<(string, bool, bool)>();
            public readonly List<(string Path, string Name, float Value)> Shapes = new List<(string, string, float)>();
        }
        sealed class MaskState
        {
            public Dictionary<string, bool> Active, Rendered; public Dictionary<string, float> Weights;
            public readonly Dictionary<string, (AnimationClip Clip, string Prefix)> Writers = new Dictionary<string, (AnimationClip, string)>();
            public readonly Dictionary<Component, bool> Reactive = new Dictionary<Component, bool>();
            public bool Visible(string path) => Shown(Active, path) && Lookup(Rendered, path, true);
        }
        sealed class MaskModel
        {
            public GameObject Root;
            public readonly List<(AnimatorStateMachine Machine, string Prefix)> Layers = new List<(AnimatorStateMachine, string)>();
            public readonly Dictionary<string, float> Defaults = new Dictionary<string, float>();
            public readonly HashSet<string> Bools = new HashSet<string>();
            public readonly Dictionary<string, bool> Active = new Dictionary<string, bool>(), Rendered = new Dictionary<string, bool>();
            public readonly Dictionary<string, float> Weights = new Dictionary<string, float>();
            public readonly List<MaskItem> Items = new List<MaskItem>();
            public readonly List<Component> Toggles = new List<Component>(), Changers = new List<Component>();
            public readonly Dictionary<AnimationClip, MaskClip> Clips = new Dictionary<AnimationClip, MaskClip>();
        }

        static void CheckBodyMasks(GameObject root, string label, JArray errors, JArray warnings)
        {
            MaskModel model;
            try { model = BuildMaskModel(root); }
            catch (Exception e) { warnings.Add(Issue("BODY_MASK_UNCHECKED", label, "没能推算换装后的身体遮罩（" + e.Message + "），请在工作台逐个切换衣服查看腿脚。")); return; }
            var defaults = EvaluateMasks(model, new Dictionary<string, float>());
            var links = MaskLinks(model, defaults);
            if (links.Count == 0) return;
            var baseline = KeptMasks(model, defaults, links);
            int reports = 0;
            if (baseline.Count > 0) { errors.Add(Issue("BODY_MASK_KEPT", label, MaskDetail("默认状态下", baseline))); reports++; }

            var seen = new HashSet<string>();
            foreach (var state in MaskStates(model))
            {
                if (reports >= MaxMaskReports || seen.Count >= MaxMaskStates) break;
                string signature = string.Join("\n", state.Value.OrderBy(v => v.Key).Select(v => v.Key + "=" + v.Value));
                if (state.Value.All(v => model.Defaults.TryGetValue(v.Key, out float d) ? Mathf.Approximately(d, v.Value) : v.Value == 0) || !seen.Add(signature)) continue;
                var kept = KeptMasks(model, EvaluateMasks(model, state.Value), links);
                foreach (var key in baseline.Keys) kept.Remove(key);
                if (kept.Count == 0) continue;
                errors.Add(Issue("BODY_MASK_KEPT", label + " · 菜单 " + state.Key, MaskDetail("切到「" + state.Key.Split('/').Last() + "」后", kept)));
                reports++;
            }
        }

        static string MaskDetail(string when, Dictionary<string, List<MaskLink>> kept)
        {
            var owners = kept.Values.SelectMany(l => l).Select(l => l.Changer ? l.Changer.gameObject.name : Leaf(l.Owner)).Distinct().ToList();
            var targets = kept.Keys.Select(k => Leaf(k.Substring(0, k.IndexOf('\n')))).Distinct();
            var shapes = kept.Keys.Select(k => k.Substring(k.IndexOf('\n') + 1)).Distinct().ToList();
            string names = string.Join("、", shapes.Take(8)) + (shapes.Count > 8 ? " 等 " + shapes.Count + " 个" : "");
            return when + " " + string.Join("、", owners.Take(6)) + (owners.Count > 6 ? " 等" : "") + " 已隐藏，但 " + string.Join("、", targets) +
                " 上给它们准备的身体遮罩还在生效（形态键 " + names + "），这些部位会缺失或变形。请在这个状态里把这些形态键复位（例如在这套衣服的菜单项上加 MA Shape Changer，Set 为 0；新衣服自己需要遮罩时按它自己的范围另设），然后在工作台实际切换这个状态检查。";
        }

        static MaskModel BuildMaskModel(GameObject root)
        {
            var model = new MaskModel { Root = root };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                model.Active[MaskPath(root, t)] = t.gameObject.activeSelf && !t.CompareTag("EditorOnly");
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                model.Rendered[MaskPath(root, renderer.transform)] = renderer.enabled;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.sharedMesh))
                for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
                    if (skin.GetBlendShapeWeight(i) != 0) model.Weights[MaskKey(MaskPath(root, skin.transform), skin.sharedMesh.GetBlendShapeName(i))] = skin.GetBlendShapeWeight(i);

            var descriptor = root.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "VRCAvatarDescriptor");
            var controllers = new List<(AnimatorController Controller, string Prefix)>();
            var merges = root.GetComponentsInChildren<Component>(true)
                .Where(c => c && c.GetType().Name == "ModularAvatarMergeAnimator" && Convert.ToString(Field(c, "layerType")) == "FX" && Field(c, "animator") is AnimatorController)
                .Select((c, i) => (c, i)).OrderBy(x => Convert.ToInt32(Field(x.c, "layerPriority") ?? 0)).ThenBy(x => x.i).Select(x => x.c).ToList();
            bool replaced = merges.Any(c => Convert.ToString(Field(c, "mergeAnimatorMode")) == "Replace");
            if (!replaced && Convert.ToBoolean(Field(descriptor, "customizeAnimationLayers") ?? false) && Field(descriptor, "baseAnimationLayers") is IEnumerable layers)
                foreach (var layer in layers)
                    if (Convert.ToString(Field(layer, "type")) == "FX" && Field(layer, "animatorController") is AnimatorController fx) controllers.Add((fx, ""));
            foreach (var merge in merges)
            {
                string prefix = "";
                if (Convert.ToString(Field(merge, "pathMode")) != "Absolute")
                {
                    string relative = Field(Field(merge, "relativePathRoot"), "referencePath") as string;
                    prefix = string.IsNullOrEmpty(relative) ? MaskPath(root, merge.transform) : relative;
                }
                controllers.Add(((AnimatorController)Field(merge, "animator"), prefix));
            }
            foreach (var (controller, prefix) in controllers)
            {
                foreach (var p in controller.parameters)
                {
                    if (p.type == AnimatorControllerParameterType.Bool || p.type == AnimatorControllerParameterType.Trigger) model.Bools.Add(p.name);
                    if (!model.Defaults.ContainsKey(p.name))
                        model.Defaults[p.name] = p.type == AnimatorControllerParameterType.Float ? p.defaultFloat : p.type == AnimatorControllerParameterType.Int ? p.defaultInt : p.defaultBool ? 1 : 0;
                }
                var all = controller.layers;
                for (int i = 0; i < all.Length; i++)
                    if (all[i].stateMachine && all[i].syncedLayerIndex < 0 && (i == 0 || all[i].defaultWeight > 0)) model.Layers.Add((all[i].stateMachine, prefix));
            }

            // VRChat starts synced parameters at the expression defaults; MA Parameters and default menu items add theirs.
            var declared = new HashSet<string>();
            if (Field(Field(descriptor, "expressionParameters"), "parameters") is IEnumerable expression)
                foreach (var p in expression)
                {
                    string name = Field(p, "name") as string;
                    if (string.IsNullOrEmpty(name)) continue;
                    model.Defaults[name] = Convert.ToSingle(Field(p, "defaultValue") ?? 0f); declared.Add(name);
                    if (Convert.ToString(Field(p, "valueType")) == "Bool") model.Bools.Add(name);
                }
            foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c && c.GetType().Name == "ModularAvatarParameters"))
                if (Field(component, "parameters") is IEnumerable list)
                    foreach (var p in list)
                    {
                        string name = Field(p, "nameOrPrefix") as string; float value = Convert.ToSingle(Field(p, "defaultValue") ?? 0f);
                        if (string.IsNullOrEmpty(name) || Convert.ToBoolean(Field(p, "isPrefix") ?? false)) continue;
                        if (Convert.ToString(Field(p, "syncType")) == "Bool") model.Bools.Add(name);
                        if (Convert.ToBoolean(Field(p, "hasExplicitDefaultValue") ?? false) || Mathf.Abs(value) > 1e-4f) { model.Defaults[name] = value; declared.Add(name); }
                    }
            foreach (var component in root.GetComponentsInChildren<Component>(true).Where(c => c))
            {
                string type = component.GetType().Name;
                if (type == "ModularAvatarObjectToggle") { model.Toggles.Add(component); continue; }
                if (type == "ModularAvatarShapeChanger") { model.Changers.Add(component); continue; }
                if (type != "ModularAvatarMenuItem") continue;
                var control = Field(component, "Control");
                string parameter = Field(Field(control, "parameter"), "name") as string;
                if (Convert.ToString(Field(control, "type")) != "Toggle" || string.IsNullOrEmpty(parameter)) continue;
                model.Items.Add(new MaskItem { Transform = component.transform, Parameter = parameter, Label = MaskPath(root, component.transform),
                    Value = Convert.ToSingle(Field(control, "value") ?? 0f), Default = Convert.ToBoolean(Field(component, "isDefault") ?? false),
                    Auto = Convert.ToBoolean(Field(component, "automaticValue") ?? false) });
            }
            // MA numbers automatic items sequentially in hierarchy order; a lone automatic toggle is simply on.
            foreach (var group in model.Items.Where(i => i.Auto).GroupBy(i => i.Parameter))
            {
                int next = 1;
                foreach (var item in group) item.Value = next++;
            }
            foreach (var item in model.Items)
            {
                if (!item.Auto && item.Value == 0 && model.Bools.Contains(item.Parameter)) item.Value = 1;
                if (item.Default && !declared.Contains(item.Parameter)) model.Defaults[item.Parameter] = item.Value;
            }
            return model;
        }

        // One state per menu toggle: MA menu items on the source avatar, plus the descriptor's own menu.
        static IEnumerable<KeyValuePair<string, Dictionary<string, float>>> MaskStates(MaskModel model)
        {
            foreach (var item in model.Items)
                yield return new KeyValuePair<string, Dictionary<string, float>>(item.Label, new Dictionary<string, float> { [item.Parameter] = item.Value });
            foreach (var control in WorkbenchGesturePreview.ReadMenu(model.Root).Where(c => c.Type == "Toggle" && !string.IsNullOrEmpty(c.Parameter)))
            {
                var values = new Dictionary<string, float>();
                foreach (var parent in control.Parents) values[parent.Key] = parent.Value;
                values[control.Parameter] = control.Value == 0 && model.Bools.Contains(control.Parameter) ? 1 : control.Value;
                yield return new KeyValuePair<string, Dictionary<string, float>>((control.Path + "/" + control.Name).Trim('/'), values);
            }
        }

        static MaskState EvaluateMasks(MaskModel model, Dictionary<string, float> selection)
        {
            var p = new Dictionary<string, float>(model.Defaults);
            foreach (var pair in selection) p[pair.Key] = pair.Value;
            var state = new MaskState { Active = new Dictionary<string, bool>(model.Active), Rendered = new Dictionary<string, bool>(model.Rendered), Weights = new Dictionary<string, float>(model.Weights) };
            var clips = new List<AnimationClip>();
            foreach (var (machine, prefix) in model.Layers)
            {
                clips.Clear(); MotionClips(Settle(machine, p), p, clips, 0);
                foreach (var clip in clips)
                {
                    var effect = ClipEffect(model, clip);
                    foreach (var a in effect.Active) (a.Renderer ? state.Rendered : state.Active)[MaskJoin(prefix, a.Path)] = a.Value;
                    foreach (var s in effect.Shapes)
                    {
                        string key = MaskKey(MaskJoin(prefix, s.Path), s.Name);
                        state.Weights[key] = s.Value; state.Writers[key] = (clip, prefix);
                    }
                }
            }
            // MA reactive components: an unselected menu item counts as inactive for everything under it.
            var selected = new Dictionary<Transform, bool>();
            foreach (var item in model.Items)
            {
                bool on = p.TryGetValue(item.Parameter, out float v) && Mathf.Abs(v - item.Value) < .01f;
                selected[item.Transform] = selected.TryGetValue(item.Transform, out bool before) ? before && on : on;
            }
            bool Enabled(Component c)
            {
                bool condition = true;
                for (var t = c.transform; t && condition; t = t == model.Root.transform ? null : t.parent)
                    condition = Lookup(state.Active, MaskPath(model.Root, t), true) && (!selected.TryGetValue(t, out bool s) || s);
                return condition != Convert.ToBoolean(Field(c, "Inverted") ?? false);
            }
            var toggled = new List<(string Path, bool Value)>();
            foreach (var toggle in model.Toggles)
            {
                bool on = state.Reactive[toggle] = Enabled(toggle);
                if (on && Field(toggle, "Objects") is IEnumerable objects)
                    foreach (var o in objects)
                    {
                        string path = Field(Field(o, "Object"), "referencePath") as string;
                        if (!string.IsNullOrEmpty(path)) toggled.Add((path, Convert.ToBoolean(Field(o, "Active"))));
                    }
            }
            foreach (var t in toggled) state.Active[t.Path] = t.Value;
            foreach (var changer in model.Changers)
                if (state.Reactive[changer] = Enabled(changer))
                    foreach (var s in ChangedShapes(changer)) { string key = MaskKey(s.Path, s.Name); state.Weights[key] = s.Value; state.Writers.Remove(key); }
            return state;
        }

        // A clip that turns a garment off and lowers a shape key on another mesh (or turns it on and raises
        // the key) shows the key is that garment's mask. MA Shape Changers raising a key own it the same way.
        static List<MaskLink> MaskLinks(MaskModel model, MaskState defaults)
        {
            var links = new List<MaskLink>();
            var pairs = new HashSet<(AnimationClip, string)>();
            foreach (var (machine, prefix) in model.Layers)
                foreach (var state in States(machine))
                {
                    var clips = new List<AnimationClip>(); AllClips(state.motion, clips, 0);
                    foreach (var clip in clips) pairs.Add((clip, prefix));
                }
            foreach (var (clip, prefix) in pairs)
            {
                var effect = ClipEffect(model, clip);
                foreach (var a in effect.Active)
                {
                    string owner = MaskJoin(prefix, a.Path);
                    if (a.Value == Lookup(a.Renderer ? defaults.Rendered : defaults.Active, owner, true)) continue;
                    foreach (var s in effect.Shapes)
                    {
                        string target = MaskJoin(prefix, s.Path);
                        if (target == owner) continue;
                        string key = MaskKey(target, s.Name); float before = Lookup(defaults.Weights, key, 0f);
                        if (a.Value && s.Value > before + 1) links.Add(new MaskLink { Owner = owner, Key = key, On = s.Value, Off = before });
                        else if (!a.Value && s.Value < before - 1) links.Add(new MaskLink { Owner = owner, Key = key, On = before, Off = s.Value });
                    }
                }
            }
            foreach (var changer in model.Changers)
                foreach (var s in ChangedShapes(changer))
                {
                    string key = MaskKey(s.Path, s.Name); float before = Lookup(model.Weights, key, 0f);
                    if (s.Value > before + 1) links.Add(new MaskLink { Changer = changer, Key = key, On = s.Value, Off = before });
                }
            return links;
        }

        // Keys still at a garment's mask value while none of the garments that own them is worn. A value
        // written in this state by an unrelated clip (a body slider, a facial expression, or the very clip
        // that hid the garment) is that clip's intent, not a leftover; only the static value or the
        // garment's own "on" clip counts.
        static Dictionary<string, List<MaskLink>> KeptMasks(MaskModel model, MaskState state, List<MaskLink> links)
        {
            var kept = new Dictionary<string, List<MaskLink>>();
            foreach (var group in links.GroupBy(l => l.Key))
            {
                float weight = Lookup(state.Weights, group.Key, 0f);
                var holding = group.Where(l => l.On - l.Off >= 1 && Mathf.Abs(weight - l.On) <= 1).ToList();
                if (holding.Count == 0 || !state.Visible(group.Key.Substring(0, group.Key.IndexOf('\n')))) continue;
                if (group.Any(l => l.Changer ? Lookup(state.Reactive, l.Changer, false) : state.Visible(l.Owner))) continue;
                var owners = new HashSet<string>(group.Where(l => !l.Changer).Select(l => l.Owner));
                if (state.Writers.TryGetValue(group.Key, out var writer) &&
                    !ClipEffect(model, writer.Clip).Active.Any(a => a.Value && owners.Contains(MaskJoin(writer.Prefix, a.Path)))) continue;
                kept[group.Key] = holding;
            }
            return kept;
        }

        static AnimatorState Enter(AnimatorStateMachine machine, Dictionary<string, float> p, int depth)
        {
            if (!machine || depth > 8) return null;
            foreach (var t in machine.entryTransitions)
                if (Holds(t.conditions, p))
                {
                    if (t.destinationState) return t.destinationState;
                    if (t.destinationStateMachine) return Enter(t.destinationStateMachine, p, depth + 1);
                }
            return machine.defaultState;
        }

        // Follows the transitions the given parameters satisfy until the layer rests (or loops).
        static Motion Settle(AnimatorStateMachine root, Dictionary<string, float> p)
        {
            var any = new List<AnimatorStateTransition>();
            var machines = new Stack<AnimatorStateMachine>(); machines.Push(root);
            while (machines.Count > 0)
            {
                var machine = machines.Pop(); any.AddRange(machine.anyStateTransitions);
                foreach (var child in machine.stateMachines) if (child.stateMachine) machines.Push(child.stateMachine);
            }
            var current = Enter(root, p, 0);
            var visited = new HashSet<AnimatorState>();
            while (current && visited.Add(current))
            {
                AnimatorState next = null;
                foreach (var t in any.Concat(current.transitions))
                {
                    if ((t.conditions.Length == 0 && !t.hasExitTime) || !Holds(t.conditions, p)) continue;
                    next = t.destinationState ? t.destinationState : t.destinationStateMachine ? Enter(t.destinationStateMachine, p, 0) : t.isExit ? Enter(root, p, 0) : null;
                    if (next == current) next = null;
                    if (next) break;
                }
                if (!next) break;
                current = next;
            }
            return current ? current.motion : null;
        }

        static bool Holds(AnimatorCondition[] conditions, Dictionary<string, float> p)
        {
            foreach (var c in conditions)
            {
                p.TryGetValue(c.parameter, out float v);
                bool ok = c.mode switch
                {
                    AnimatorConditionMode.If => v != 0,
                    AnimatorConditionMode.IfNot => v == 0,
                    AnimatorConditionMode.Greater => v > c.threshold,
                    AnimatorConditionMode.Less => v < c.threshold,
                    AnimatorConditionMode.Equals => Mathf.Approximately(v, c.threshold),
                    AnimatorConditionMode.NotEqual => !Mathf.Approximately(v, c.threshold),
                    _ => false
                };
                if (!ok) return false;
            }
            return true;
        }

        // The clips a motion plays at these parameters; 1D/2D trees take the nearest child.
        static void MotionClips(Motion motion, Dictionary<string, float> p, List<AnimationClip> into, int depth)
        {
            if (!motion || depth > 8) return;
            if (motion is AnimationClip clip) { into.Add(clip); return; }
            if (!(motion is BlendTree tree) || tree.children.Length == 0) return;
            p.TryGetValue(tree.blendParameter, out float x); p.TryGetValue(tree.blendParameterY, out float y);
            if (tree.blendType == BlendTreeType.Direct)
            {
                foreach (var child in tree.children)
                    if (p.TryGetValue(child.directBlendParameter, out float w) && w > .01f) MotionClips(child.motion, p, into, depth + 1);
            }
            else if (tree.blendType == BlendTreeType.Simple1D) MotionClips(tree.children.OrderBy(c => Mathf.Abs(c.threshold - x)).First().motion, p, into, depth + 1);
            else MotionClips(tree.children.OrderBy(c => (c.position - new Vector2(x, y)).sqrMagnitude).First().motion, p, into, depth + 1);
        }

        static void AllClips(Motion motion, List<AnimationClip> into, int depth)
        {
            if (!motion || depth > 8) return;
            if (motion is AnimationClip clip) into.Add(clip);
            else if (motion is BlendTree tree) foreach (var child in tree.children) AllClips(child.motion, into, depth + 1);
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine root)
        {
            var machines = new Stack<AnimatorStateMachine>(); machines.Push(root);
            while (machines.Count > 0)
            {
                var machine = machines.Pop();
                foreach (var child in machine.states) if (child.state) yield return child.state;
                foreach (var child in machine.stateMachines) if (child.stateMachine) machines.Push(child.stateMachine);
            }
        }

        static MaskClip ClipEffect(MaskModel model, AnimationClip clip)
        {
            if (model.Clips.TryGetValue(clip, out var effect)) return effect;
            effect = new MaskClip();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                bool active = binding.propertyName == "m_IsActive" && binding.type == typeof(GameObject);
                bool rendered = binding.propertyName == "m_Enabled" && typeof(Renderer).IsAssignableFrom(binding.type);
                bool shape = binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);
                if (!active && !rendered && !shape) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0) continue;
                float value = curve.Evaluate(0);
                if (active || rendered) effect.Active.Add((binding.path, value > .5f, rendered));
                else effect.Shapes.Add((binding.path, binding.propertyName.Substring("blendShape.".Length), value));
            }
            return model.Clips[clip] = effect;
        }

        static IEnumerable<(string Path, string Name, float Value)> ChangedShapes(Component changer)
        {
            if (!(Field(changer, "Shapes") is IEnumerable shapes)) yield break;
            foreach (var s in shapes)
            {
                string path = Field(Field(s, "Object"), "referencePath") as string, name = Field(s, "ShapeName") as string;
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name)) continue;
                yield return (path, name, Convert.ToString(Field(s, "ChangeType")) == "Delete" ? 100f : Convert.ToSingle(Field(s, "Value") ?? 0f));
            }
        }

        static bool Shown(Dictionary<string, bool> active, string path)
        {
            for (int i = path.IndexOf('/'); ; i = path.IndexOf('/', i + 1))
            {
                if (active.TryGetValue(i < 0 ? path : path.Substring(0, i), out bool on) && !on) return false;
                if (i < 0) return true;
            }
        }
        static T Lookup<TKey, T>(Dictionary<TKey, T> map, TKey key, T fallback) => map.TryGetValue(key, out var value) ? value : fallback;
        static string MaskPath(GameObject root, Transform t) => AnimationUtility.CalculateTransformPath(t, root.transform);
        static string MaskJoin(string prefix, string path) => string.IsNullOrEmpty(prefix) ? path : string.IsNullOrEmpty(path) ? prefix : prefix + "/" + path;
        static string MaskKey(string path, string shape) => path + "\n" + shape;
        static string Leaf(string path) => path.Substring(path.LastIndexOf('/') + 1);
    }
}

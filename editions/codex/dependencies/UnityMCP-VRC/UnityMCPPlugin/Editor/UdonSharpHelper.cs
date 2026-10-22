using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

namespace UnityMCP.VRChatUtils
{
    /// <summary>
    /// Attach fully-wired UdonSharp components to GameObjects from code.
    ///
    /// Adding a working Udon script programmatically is finicky because a live Udon component is THREE linked
    /// objects, and the supported API to build them is easy to miss:
    ///   1. A <c>UdonSharpProgramAsset</c> (the compiled program). A brand-new U# script has none, and nothing
    ///      auto-creates it; <c>AddUdonSharpComponent</c> throws "Unable to find valid U# program asset" until
    ///      one exists. So we create it exactly like the U# inspector does and compile it.
    ///   2. A backing <c>VRC.Udon.UdonBehaviour</c> that points at the program asset. Plain
    ///      <c>GameObject.AddComponent&lt;T&gt;()</c> gives only the inert proxy and NO backing - it won't run.
    ///   3. The proxy <c>UdonSharpBehaviour</c> you see in the inspector.
    /// The builder is <c>GameObject.AddUdonSharpComponent(type)</c> - an EXTENSION METHOD in namespace
    /// <c>UdonSharpEditor</c> (class UdonSharpComponentExtensions), NOT a static on UdonSharpEditorUtility, so
    /// enumerating UdonSharpEditorUtility never finds it. Finally, public field values only reach the running
    /// program after <c>UdonSharpEditorUtility.CopyProxyToUdon(proxy)</c> serialises them into the Udon heap.
    ///
    /// This helper does all of that. It calls UdonSharp purely by REFLECTION so the UnityMCP.Editor assembly
    /// keeps NO compile-time dependency on the VRChat SDK (it still builds in non-VRChat projects); the Udon
    /// methods simply throw a clear error if UdonSharp isn't installed.
    /// </summary>
    public static class UdonSharpHelper
    {
        // ---- one-call API ------------------------------------------------------------------------------

        /// <summary>
        /// Attach a fully-wired UdonSharp component to <paramref name="target"/> and return the proxy
        /// UdonSharpBehaviour (as a Component). Creates+compiles the program asset if missing, adds the proxy +
        /// backing UdonBehaviour, applies <paramref name="fieldValues"/> to the proxy's public fields, and
        /// serialises them into the Udon heap with CopyProxyToUdon.
        /// </summary>
        /// <param name="target">GameObject to add the component to.</param>
        /// <param name="udonSharpType">A UdonSharpBehaviour subclass.</param>
        /// <param name="fieldValues">Optional public-field name -> value to set on the proxy before serialising.</param>
        public static Component AddUdonSharpComponent(GameObject target, Type udonSharpType, IDictionary<string, object> fieldValues = null)
        {
            RequireUdonSharp();
            if (target == null) throw new ArgumentNullException(nameof(target));

            // step 1 - the program asset. A program asset that was JUST created CANNOT be attached in the same
            // call: UdonSharp only reconciles a new program's compiled "script version" after control returns to
            // the editor loop (verified - neither CompileSync nor forcing the manager's update pass collapses it
            // into one synchronous call). So if we have to create it, create+compile and ask the caller to run
            // again; the follow-up call (a fresh command / next editor tick) attaches cleanly. An already-existing
            // program asset attaches in this one call.
            bool existed = ProgramAssetFor(udonSharpType) != null;
            EnsureProgramAsset(udonSharpType);
            if (!existed)
                throw new InvalidOperationException(
                    $"Created + compiled the UdonSharp program asset for {udonSharpType.Name}. UdonSharp finalizes a " +
                    "new program on the next editor tick, so the component can't be attached in this same command - " +
                    "call AddUdonSharpComponent again (a separate execute_editor_command) to attach it.");

            // step 2 - the supported builder (extension method) makes the proxy + backing UdonBehaviour and links
            // the now-existing program asset.
            var add = ExtensionsType.GetMethod("AddUdonSharpComponent", BindingFlags.Public | BindingFlags.Static,
                                               null, new[] { typeof(GameObject), typeof(Type) }, null);
            if (add == null) throw new MissingMethodException("UdonSharpComponentExtensions.AddUdonSharpComponent(GameObject, Type) not found.");
            var proxy = add.Invoke(null, new object[] { target, udonSharpType }) as Component;
            if (proxy == null) throw new InvalidOperationException($"AddUdonSharpComponent returned null for {udonSharpType.Name}.");

            if (fieldValues != null)
            {
                foreach (var kv in fieldValues)
                {
                    var f = udonSharpType.GetField(kv.Key, BindingFlags.Public | BindingFlags.Instance);
                    if (f == null) { Debug.LogWarning($"[UdonSharpHelper] {udonSharpType.Name} has no public field '{kv.Key}'; skipped."); continue; }
                    f.SetValue(proxy, kv.Value);
                }
            }

            // step 3 - push the proxy's field values into the UdonBehaviour heap, or they're lost at runtime.
            var copy = EditorUtilType.GetMethod("CopyProxyToUdon", BindingFlags.Public | BindingFlags.Static,
                                                null, new[] { UdonSharpBehaviourType }, null);
            copy.Invoke(null, new object[] { proxy });

            EditorUtility.SetDirty(proxy);
            return proxy;
        }

        /// <summary>Overload that resolves <paramref name="udonSharpTypeName"/> (simple or full name) via its script asset.</summary>
        public static Component AddUdonSharpComponent(GameObject target, string udonSharpTypeName, IDictionary<string, object> fieldValues = null)
            => AddUdonSharpComponent(target, ResolveUdonSharpType(udonSharpTypeName), fieldValues);

        // ---- program-asset management ------------------------------------------------------------------

        /// <summary>
        /// Ensure a compiled <c>UdonSharpProgramAsset</c> exists for <paramref name="udonSharpType"/>, creating
        /// and compiling one next to its .cs if missing. Returns the program asset (as UnityEngine.Object).
        /// </summary>
        public static UnityEngine.Object EnsureProgramAsset(Type udonSharpType)
        {
            RequireUdonSharp();
            if (udonSharpType == null) throw new ArgumentNullException(nameof(udonSharpType));
            if (!UdonSharpBehaviourType.IsAssignableFrom(udonSharpType))
                throw new ArgumentException($"{udonSharpType.Name} is not a UdonSharpBehaviour subclass.");

            var getForClass = ProgramAssetType.GetMethod("GetProgramAssetForClass", BindingFlags.Public | BindingFlags.Static);
            var existing = getForClass.Invoke(null, new object[] { udonSharpType }) as UnityEngine.Object;
            if (existing != null) return existing;

            string scriptPath = FindScriptPath(udonSharpType);
            if (scriptPath == null)
                throw new InvalidOperationException($"Could not find the .cs asset for {udonSharpType.Name} (has it compiled yet?).");

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            var programAsset = ScriptableObject.CreateInstance(ProgramAssetType);
            var srcField = ProgramAssetType.GetField("sourceCsScript", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            srcField.SetValue(programAsset, monoScript);

            string assetPath = Path.ChangeExtension(scriptPath, ".asset");
            AssetDatabase.CreateAsset(programAsset, assetPath);
            AssetDatabase.Refresh();

            // Compile so the program asset carries a serialized Udon program; force, so a brand-new asset builds now.
            var compile = ProgramAssetType.GetMethod("CompileAllCsPrograms", BindingFlags.Public | BindingFlags.Static);
            if (compile != null)
            {
                var ps = compile.GetParameters();
                var args = ps.Select(p => p.HasDefaultValue ? p.DefaultValue : (object)false).ToArray();
                if (args.Length >= 1) args[0] = true;   // forceCompile
                compile.Invoke(null, args);
            }

            // The program-asset lookup (GetAllUdonSharpPrograms) caches its list and only rebuilds when the
            // cache is cleared, so a just-created asset can be invisible to AddUdonSharpComponent in the SAME
            // call (its RunBehaviourSetup would then null-ref). Force a refresh.
            var clearCache = ProgramAssetType.GetMethod("ClearProgramAssetCache", BindingFlags.NonPublic | BindingFlags.Static);
            clearCache?.Invoke(null, null);

            Debug.Log($"[UdonSharpHelper] created + compiled UdonSharp program asset {assetPath}");
            return getForClass.Invoke(null, new object[] { udonSharpType }) as UnityEngine.Object;
        }

        /// <summary>
        /// Back-compat: create the program asset for a U# script by PATH. Now built on the supported API instead
        /// of hand-written YAML. Prefer <see cref="AddUdonSharpComponent(GameObject, Type, IDictionary{string, object})"/>
        /// to actually attach a working component.
        /// </summary>
        [Obsolete("Creates only the program asset. Use AddUdonSharpComponent(go, type, fields) to attach a fully-wired component.")]
        public static string CreateAsset(string scriptPath)
        {
            RequireUdonSharp();
            if (string.IsNullOrEmpty(scriptPath) || !scriptPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid script path: " + scriptPath);

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            var cls = monoScript != null ? monoScript.GetClass() : null;
            if (cls == null)
                throw new InvalidOperationException($"No compiled UdonSharpBehaviour at {scriptPath} (it must compile before a program asset can be made).");

            EnsureProgramAsset(cls);
            return Path.ChangeExtension(scriptPath, ".asset");
        }

        // ---- reflection plumbing ----------------------------------------------------------------------

        static Type ProgramAssetType        => FindType("UdonSharp.UdonSharpProgramAsset");
        static Type ExtensionsType          => FindType("UdonSharpEditor.UdonSharpComponentExtensions");
        static Type EditorUtilType          => FindType("UdonSharpEditor.UdonSharpEditorUtility");
        static Type UdonSharpBehaviourType  => FindType("UdonSharp.UdonSharpBehaviour");

        static void RequireUdonSharp()
        {
            if (ProgramAssetType == null || ExtensionsType == null || EditorUtilType == null || UdonSharpBehaviourType == null)
                throw new InvalidOperationException("UdonSharp (com.vrchat.worlds) is not present in this project; cannot create/attach Udon components.");
        }

        // The UdonSharpProgramAsset for a type, or null (UdonSharpProgramAsset.GetProgramAssetForClass).
        static UnityEngine.Object ProgramAssetFor(Type udonSharpType)
        {
            var m = ProgramAssetType.GetMethod("GetProgramAssetForClass", BindingFlags.Public | BindingFlags.Static);
            return m.Invoke(null, new object[] { udonSharpType }) as UnityEngine.Object;
        }

        // Resolve a U# type by name via its SCRIPT ASSET (MonoScript.GetClass()), not by scanning loaded
        // assemblies. A long editor session with domain reloads can keep stale duplicate Assembly-CSharp
        // copies alive, so an assembly scan finds the same logical type several times and wrongly reports it
        // as "ambiguous". The script asset always maps to the current type.
        static Type ResolveUdonSharpType(string name)
        {
            RequireUdonSharp();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Type name is empty.");

            foreach (var guid in AssetDatabase.FindAssets($"{LeafName(name)} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                var cls = ms != null ? ms.GetClass() : null;
                if (cls == null || !UdonSharpBehaviourType.IsAssignableFrom(cls)) continue;
                if (cls.Name == name || cls.FullName == name) return cls;
            }
            throw new ArgumentException($"No UdonSharpBehaviour script named '{name}' found (expected {LeafName(name)}.cs whose class derives from UdonSharpBehaviour).");
        }

        static string LeafName(string typeName)
        {
            int dot = typeName.LastIndexOf('.');
            return dot >= 0 ? typeName.Substring(dot + 1) : typeName;
        }

        static string FindScriptPath(Type type)
        {
            foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith($"/{type.Name}.cs", StringComparison.Ordinal)) continue;
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (ms != null && ms.GetClass() == type) return path;
            }
            return null;
        }

        static Type FindType(string fullName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = a.GetType(fullName); } catch { continue; }
                if (t != null) return t;
            }
            return null;
        }
    }
}

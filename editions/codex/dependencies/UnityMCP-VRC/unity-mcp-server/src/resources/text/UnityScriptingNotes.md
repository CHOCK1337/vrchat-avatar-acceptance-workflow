<!-- description: C# gotchas for execute_editor_command snippets — applies to every Unity project (VRChat, Basis, plain). -->
# Unity C# scripting notes (for execute_editor_command)

Gotchas when writing C# that runs in the Editor via `execute_editor_command` — what won't
compile, and what compiles but bites silently (the code *looks* like it worked).

## Language level depends on the project's API profile

On **.NET Framework-profile projects** (the Unity 2022-era / VRChat norm) commands compile
with Mono's compiler at language version **C# 7.0**, so newer syntax is a hard compile
error. On **.NET Standard-profile projects** (the Unity 6 default) commands compile with
the Editor's bundled Roslyn at `-langversion:latest` and modern C# just works — the C# 7.0
rules below don't apply. If you don't know which profile the project uses, write
C# 7.0-style code; it compiles on both.

The C# 7.0 rules: the most common trip-up is the **bare `default` literal isn't allowed** —
write the typed form `default(T)`:

```csharp
int n = default;            // ERROR (C# 7.1 feature)
CancellationToken ct = default;
int n = default(int);                       // OK
CancellationToken ct = default(CancellationToken);   // OK
```

Other newer features that won't compile here — use the C# 7.0 equivalent instead:

- **switch expressions** (`x switch { ... }`, C# 8) → use a classic `switch` statement.
- **`using` declarations** (`using var x = ...;`, C# 8) → use a `using (...) { }` block.
- **ranges/indices** (`arr[^1]`, `arr[1..3]`, C# 8) → index/`GetRange` explicitly.
- **`??=`** (C# 8) → `if (x == null) x = ...;` (and mind the fake-null caveat below).
- **target-typed `new()`** (C# 9) → name the type: `new Foo()`.

`default(T)` aside, prefer plain, conservative C# and you'll stay inside 7.0. The
compiler rejects newer syntax with a recognizable message — `CS1644: Feature '…' cannot
be used because it is not part of the C# 7.0 language specification`, or a parse error
like `CS1525: Unexpected symbol`. If you see either, downgrade the syntax rather than
retrying the same code.

### Local functions and statement-bodied lambdas don't parse (Mono backend)

On .NET Framework-profile projects the executor compiles with the **Mono
`CSharpCodeProvider` (mcs)**, which — even though local functions are a C# 7.0 feature —
does **not** accept them, and chokes on statement-bodied lambdas too. (The Roslyn backend
on .NET Standard projects accepts both.) They surface as a cascade of nonsense parse errors (`CS1525`, `CS1519`,
`Primary constructor body is not allowed`, `Identifier expected, '=>' is a keyword`) pointing
at the function/lambda line. Hoist them to **static methods on the `EditorCommand` class**:

```csharp
// WRONG — local function; mcs reports a pile of CS15xx errors at this line
Type Find(string n) { foreach (var a in ...) { ... } return null; }

// WRONG — statement-bodied lambda inside LINQ
asms.Select(a => { try { return a.GetType(n); } catch { return null; } })

// CORRECT — a static helper method, called normally
public class EditorCommand {
    static Type Find(string n) { foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) {
        var t = a.GetType(n); if (t != null) return t; } return null; }
    public static object Execute() { var t = Find("VRC.Udon.UdonBehaviour"); ... }
}
```

Expression-bodied lambdas (`x => x.Name`, `(a,b) => a.CompareTo(b)`) are fine — it's only the
`{ ... }`-bodied lambdas and local functions that fail.

## What's referenced (and the CS1070 trap)

The full base class library is available — `System.Collections.Generic` (`List<T>`,
`Dictionary<,>`, `HashSet<T>`, …), `System.Linq`, `System.IO`, `System.Text`,
`System.Threading.Tasks`, `System.Reflection`, and so on — alongside all `UnityEngine`
modules, `UnityEditor`, installed `Unity.*` packages, the VRChat/UdonSharp assemblies, the
`Basis*` framework assemblies, and the project's own `Assembly-CSharp` /
`Assembly-CSharp-Editor` types. **`Newtonsoft.Json`** is
also referenced, so `using Newtonsoft.Json;` works for richer JSON than `JsonUtility` (which
can't do dictionaries, polymorphism, or top-level arrays). You still have to add the matching
`using` directives yourself; nothing is implicitly imported.

If a type that *should* exist fails with **`CS1070` ("type forwarded to an assembly that is
not referenced")**, it usually means a BCL facade assembly isn't on the reference list.
This was the case for `HashSet<T>` before the reference set was broadened. If you still hit
it for some exotic type, fall back to a referenced equivalent (e.g. a `List<T>` plus a
`Contains` check instead of the unavailable set type) rather than retrying the same code.

## Unity's "fake null" defeats `??`, `?.`, and `is null`

`UnityEngine.Object` (so `GameObject`, `Component`, `MonoBehaviour`, assets, …) overloads
`==`/`!=` to report a *destroyed or missing* object as equal to `null`. That overload is the
only thing that knows about the fake-null state — the C# null operators bypass it:

- `obj ?? fallback` does **not** fall back for a fake-null Unity object.
- `obj?.Foo()` does **not** short-circuit; it calls through and can throw.
- `obj is null` / `ReferenceEquals(obj, null)` report **false** for a fake-null object.

**The trap that started this note:**

```csharp
// WRONG: ?? sees a non-real reference, so it never adds the component, and a
// later access throws MissingComponentException.
var mr = go.GetComponent<MeshRenderer>() ?? go.AddComponent<MeshRenderer>();
```

**Do this instead — use the overloaded `==` via an explicit check:**

```csharp
var mr = go.GetComponent<MeshRenderer>();
if (mr == null) mr = go.AddComponent<MeshRenderer>();   // == is the Unity-aware comparison
```

Or `GetComponent` + `TryGetComponent` (which returns a real bool):

```csharp
if (!go.TryGetComponent<MeshRenderer>(out var mr)) mr = go.AddComponent<MeshRenderer>();
```

Rule of thumb: for anything deriving from `UnityEngine.Object`, test with `== null` /
`!= null`, never with `??`, `?.`, `is null`, or `ReferenceEquals`.

## Other quiet failure modes

- **`GetComponent<T>()` returns real `null`** (not fake-null) when the component is absent,
  so the `== null` check above is correct for both the "missing" and "destroyed" cases.
- **Edits aren't persisted automatically.** After changing a scene object call
  `EditorUtility.SetDirty(obj)` (and `EditorSceneManager.MarkSceneDirty(...)` for scene
  changes); for assets, `AssetDatabase.SaveAssets()`. Otherwise the change can be lost on
  reload and won't show up in a later read.
- **Writing a `.cs` file into `Assets/` triggers a recompile + domain reload.** That tears
  the MCP connection down briefly — expect the *next* call to retry/reconnect. Do the file
  write as the last step of a command, not mid-sequence with more work depending on it.
- **Return JSON-serializable data.** `EditorCommand.Execute()`'s return value is serialized
  back to the caller; hand back plain values / anonymous objects (ids, names, counts, paths),
  not live `UnityEngine.Object` references.
- **Overwriting an asset file in place may not reimport.** `AssetDatabase.Refresh()` picks up
  *new* files and ones Unity decides have changed, but copying a new `Props.obj` over an old
  one doesn't reliably trigger a reimport — Unity may keep serving the cached import. Force the
  specific asset(s) explicitly:

  ```csharp
  AssetDatabase.ImportAsset("Assets/Models/Props.obj",
                            ImportAssetOptions.ForceUpdate);   // re-imports even if unchanged
  ```

  The path is project-relative, starts with `Assets/`, and uses forward slashes. Use this
  rather than `Refresh()` whenever you've replaced an existing asset's bytes on disk.

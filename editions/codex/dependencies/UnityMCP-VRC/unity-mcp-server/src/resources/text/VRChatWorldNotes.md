<!-- description: VRChat world building: project layout, the UdonSharp component-attach dance, and workflow conventions. -->
# VRChat World Unity Project Notes

## Project Structure
- Put all new assets in the `Assets/Game/` folder
- Before making edits, examine existing components to understand what's already in place

## Development Best Practices
- Use multiple `execute_editor_commands` instead of trying to do everything in one command
- Unity and UdonSharp are complex environments with many potential issues
- Break down your tasks into smaller, more manageable commands

## Useful Commands

### Attaching a UdonSharp script to a GameObject (the reliable way)

Adding a working Udon component is finicky: a live Udon component is THREE linked objects — a
`UdonSharpProgramAsset` (the compiled program), a backing `VRC.Udon.UdonBehaviour` that points at it, and the
proxy `UdonSharpBehaviour` you see. Plain `gameObject.AddComponent<MyUSharpBehaviour>()` gives you ONLY the
inert proxy (no program, no backing) so it does nothing in-world. The supported builder is the extension
method `gameObject.AddUdonSharpComponent(type)` in namespace `UdonSharpEditor` — easy to miss because it is
NOT a static on `UdonSharpEditorUtility` — and it does NOT create the program asset (a new script has none),
so it throws *"Unable to find valid U# program asset"* until one exists.

`UdonSharpHelper` does the whole dance for you (create+compile the program asset → add proxy + backing
UdonBehaviour → set public fields → `CopyProxyToUdon`). One call:

```csharp
var go = GameObject.Find("MyObject");
var fields = new System.Collections.Generic.Dictionary<string, object> {
    { "speed", 90f },
    { "axis",  UnityEngine.Vector3.up },
};
UnityMCP.VRChatUtils.UdonSharpHelper.AddUdonSharpComponent(go, "MyUSharpBehaviour", fields);
```

Notes:
- **A brand-new script type needs TWO passes.** The *first* time a given U# script is used in the project it
  has no program asset; the helper creates+compiles one and **throws** `"Created … call AddUdonSharpComponent
  again"` instead of attaching — because UdonSharp only finalizes a new program's compiled "script version"
  after control returns to the editor loop (you can't force it in-call). Just call it **again** in a separate
  `execute_editor_command` and it attaches. Once the program asset exists it's a single call. For many objects,
  call `EnsureProgramAsset(type)` once, then attach them all on the next pass.
- The script must have **compiled first** — write the `.cs`, let the recompile/domain reload finish, THEN
  attach in a SEPARATE `execute_editor_command` (a fresh `.cs` doesn't exist as a type until it compiles).
- `fields` keys are the proxy's **public field names**; values must be Udon-serializable
  (`float`, `int`, `bool`, `string`, `Vector3`, `Color`, `UnityEngine.Object` refs, …).
- Idempotency / state are your job — check `go.GetComponent("MyUSharpBehaviour")` before re-adding.
- `EnsureProgramAsset(type)` and the legacy `CreateAsset(path)` are also available if you only need the
  program asset; `CreateAsset` no longer hand-writes YAML, it uses the supported API.

### Compiling the Project
To compile the entire project and check for errors in the code:
```csharp
CompilationPipeline.RequestScriptCompilation();
```

## Rendering Text in Udon

Legacy Unity TextMesh (or UI.Text) components can't be used directly in Udon because many of their methods and properties aren't exposed. Instead:

- Use TextMeshPro-based components that are supported by VRChat's Udon
- If TextMeshPro is not installed, it can be added via the Unity Package Manager

## Code Guidelines
- Don't end lines with `\\` to continue a string to the next line as that is not valid C#
- If unsure about implementation, ask for clarification
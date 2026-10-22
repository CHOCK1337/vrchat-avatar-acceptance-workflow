<!-- description: Basis framework: connecting, worlds and spawning, networking, and driving Basis via execute_editor_command. -->
# Basis Framework Unity Project Notes

Operational notes for building and driving a [Basis](https://github.com/BasisVR/Basis) world — the
open-source social-VR framework — via `execute_editor_command`. Basis is **Unity 6 + URP** with plain C#
**MonoBehaviours** (no Udon), plus a standalone .NET server. If you're in a VRChat/Udon project instead,
read `VRChatWorldNotes` / `UdonSharp`, not this.

## The big difference from VRChat: no Udon

Scripts are ordinary C# MonoBehaviours compiled into the (forked) client. There is **no Udon dance** — a
component is one object, `gameObject.AddComponent<MyBehaviour>()` just works, with real `try`/catch, threads,
and the full API. Networked scripts derive from `BasisNetworkBehaviour` and use a **message + ownership**
model (`SendCustomNetworkEvent`, `OnNetworkMessage`, `TakeOwnership`, `OnPlayerJoined`) rather than
`[UdonSynced]` variables. A sandboxed CIL interpreter (Cilbox) exists for scripts shipped inside downloaded
content, but is experimental and slow — a client you build yourself doesn't need it.

## Rendering: URP

The project is **URP**. Built-in-RP materials/shaders render **magenta**; use URP shaders
(`Universal Render Pipeline/Lit`, etc.) or port shaders to URP.

## Worlds and spawning

- A **world is a scene** containing a GameObject with a `BasisScene` component. Key fields: `SpawnPoint`
  (Transform), `RespawnHeight` (float — fall below it and you respawn), `RespawnCheckTimer`, `MainCamera`.
- When **no** world `BasisScene` is active, Basis loads its **loading scene** (an empty environment). Seeing a
  featureless void after connecting usually means no world scene is active yet, not a failure.
- `BasisSceneFactory` (static) owns the active world: `BasisSceneFactory.BasisScene` is the current one, and
  `BasisSceneFactory.SpawnPlayer(localPlayer)` teleports the player to that scene's `SpawnPoint`
  (via `RequestSpawnPoint`). To force a specific scene active from a command, set
  `BasisSceneFactory.BasisScene` to its `BasisScene`, then `SpawnPlayer`. If a *different* scene's respawn is
  yanking the player back, also raise/lower `BasisSceneFactory.RespawnHeight`.
- The **local default world** loads via the boot-content / `BundledContentHolder` mechanism (a scene in the
  project's `Assets/`, not a server download). That's what appears when you connect to a local server.

## Player

- `BasisLocalPlayer.Instance` is the local player; `.PlayerSelf` is its root `Transform`.
- Move it with `BasisLocalPlayer.Instance.Teleport(position, rotation, BasisTeleportMode.WorldFeet)`
  (the `WorldFeet` mode places the feet at `position`). Grounded state is on its `CharacterController`.

## Connecting to a server

- **Normal path:** open the Servers menu, enter the address in the Advanced panel (or pick a saved entry),
  Connect. Client and server **must be the same version** — build both from one checkout.
- **Programmatically** (from a command): `BasisConnectionService.ConnectAsync(entry, userName, isHostMode)`
  where `entry` is a `ServerDirectoryEntry` whose `Target` is a `ConnectionTarget(stackId, "ip:port")` with
  its **Address / Port / Password keys explicitly `Set`** — the raw string alone yields an empty-IP
  `ConnectionFailed`. The default network stack id is `litenetlib`
  (`BasisNetworkStackRegistry.DefaultId`). `BasisNetworkConnection.LocalPlayerIsConnected` reports state.
- Auth is DID-based; a first connection can log one `Authentication timeout` then succeed on retry.
- `BasisNetworkManagement.IsInitialized` is false until the client finishes booting — wait for the menu
  before connecting.

## The menu

`BasisMainMenu` is a **static** world-space menu, not a MonoBehaviour you'll find by scene name. Drive it with
`BasisMainMenu.Open()`, `OpenWithProvider(title)`, `Close()`, `Toggle()`. To reveal the world for a
screenshot, call `BasisMainMenu.Close()`.

## Driving it via execute_editor_command

- Prefer several small commands over one giant one.
- A forced domain reload (e.g. `EditorUtility.RequestScriptReload()`) briefly drops the plugin's server; it
  re-registers a few seconds after the reload finishes — re-`list_unity_instances` if a call fails meanwhile.
- Much of the framework is static singletons (`BasisSceneFactory`, `BasisMainMenu`, `BasisLocalPlayer.Instance`,
  `BasisConnectionService`, `BasisNetworkStackRegistry`), so a lot can be inspected and driven by reflection
  without holding a scene reference.

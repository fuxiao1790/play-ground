---
name: vfx-graph-agent
description: Read, create, and modify VFX Graphs through the AgentVFX bridge and Unity CLI instead of hand-editing .vfx serialization
user-invocable: true
---

# VFX Graph Agent

## Purpose
Let an agent inspect and modify VFX Graphs by calling Unity's own editor model
through the AgentVFX bridge (`Assets/AgentVFX/`) via `unity command`. Unity
remains the only writer of `.vfx` files — every command below mutates the live
in-memory graph, then asks Unity to compile/serialize it. Never open a `.vfx`
file as text and edit its YAML directly; that bypasses Unity's own validation
and is exactly what this bridge exists to avoid.

Design record and rationale: `.agent/vfx-graph-agent-bridge/index.md`.
Bridge source: `Assets/AgentVFX/InternalAccess/` (internal VFX Graph API calls,
one folder, nowhere else), `Assets/AgentVFX/Editor/` (DTOs + stable API),
`Assets/AgentVFX/Commands/` (`[CliCommand]` surface).

## Before Doing Anything: Confirm Connectivity
```powershell
unity pipeline list
```
Must show `Server Reachable: true` for this project. If not, the Unity Editor
isn't open, or hasn't finished importing/compiling yet — open it and wait, or
ask the user to. Do not proceed on a guess that it's ready.

```powershell
unity command vfx_ping
```
Should return `UnityEditor.VFX.VFXGraph`. If this fails but `unity pipeline
list` shows reachable, something in `Assets/AgentVFX/` failed to compile —
check the Unity Console / `Editor.log`, fix it, then:
```powershell
unity command recompile
unity command recompile_status   # poll until "status":"completed"
```
A recompile is a domain reload: every bridge-assigned node/slot id
(`node:N`, `slot:N`) resets. Always re-run `vfx_graph_read` for fresh ids
after any recompile — don't reuse ids from before one.

## Critical Safety Rule: Never Target Production Content For Experiments Or Tests
The bridge's path guard (`AgentVfxPathGuard`) only allows
`Assets/Vfx/` (this project's real, hand-authored graphs) and
`Assets/AgentGenerated/` (agent scratch space). That guard exists to bound
*accidental* damage, but it does not distinguish "the user asked me to change
this real graph" from "I want a graph to poke at while I figure out an API
call" — that distinction is the agent's job, not the guard's.

- **Real, requested changes to a real graph**: operate on its actual
  `Assets/Vfx/...` path, as asked.
- **Anything exploratory, or any test** (validating the bridge itself,
  trying an unfamiliar node type, checking whether a mutation works before
  doing it for real): copy the graph first, work on the copy.

To copy a real graph into scratch space without hand-touching its
serialization, use Unity's own import command (not a filesystem copy tool —
see the incident note below for why):
```powershell
unity command import_asset --source "Assets/Vfx/LineSeg/MagicBoltTrail.vfx" --path "Assets/AgentGenerated/TestFixtures/MagicBoltTrail.vfx" --confirm true
```
This gives the copy its own GUID and imports it properly. `AgentVfxCompatibilityTests.CanReadRealProductionGraph`
uses exactly this pattern — a checked-in fixture, not a live read of
`Assets/Vfx/`.

**Incident note**: an earlier session ran `rm -rf .../Assets/VFX` to clean up
a scaffold folder and deleted every real graph under `Assets/Vfx/` — Windows
filesystems are case-insensitive, so `VFX` and `Vfx` are the *same directory*
on disk. Recovered via `git checkout -- Assets/Vfx`, no data lost, but it
should not have been possible: don't run filesystem-level delete/move
commands against anything under `Assets/Vfx/` or any path whose casing you
haven't confirmed against `git status`/a directory listing first. Prefer the
bridge's own commands (which go through Unity's AssetDatabase) over raw
filesystem operations whenever an equivalent bridge command exists.

## Command Reference
All commands below run as `unity command <name> [args...]`, positional
arguments in this order, `--json` for machine-readable output. Every asset
path argument is checked against the safety rule above by the bridge itself
(`Assets/Vfx/` or `Assets/AgentGenerated/` only).

| Command | Args | Returns |
|---|---|---|
| `vfx_ping` | — | Connectivity smoke test |
| `vfx_graph_create` | `assetPath` | Creates an empty `.vfx` |
| `vfx_graph_read` | `assetPath` | Full node/connection topology (JSON) |
| `vfx_graph_save` | `assetPath` | Serializes current in-memory state to disk |
| `vfx_types_list` | — | Every Context/Block/Operator type Unity's VFX Graph exposes right now — never assume a type from memory, always list it |
| `vfx_type_describe` | `typeId` | Settings/input/output slot names; for a Block, `validContexts` — check this before `vfx_node_create`ing a block into a context, or expect a rejected creation |
| `vfx_node_create` | `assetPath typeId parentId x y` | New node id. `parentId` empty/omitted = top-level (graph); a context id = block goes inside that context |
| `vfx_node_delete` | `nodeId` | — |
| `vfx_node_move` | `nodeId x y` | — |
| `vfx_node_configure` | `nodeId settingName valueJson` | Sets a *setting* (a non-linkable field baked into the node, e.g. which attribute a SetAttribute block targets) — not a slot |
| `vfx_slot_read` | `slotId` | Name/type/value/link state |
| `vfx_slot_set` | `slotId valueJson` | Sets a slot's direct value |
| `vfx_slot_connect` | `fromSlotId toSlotId` | Links an output slot to an input slot; returns `{success, message}` instead of throwing on an incompatible link |
| `vfx_slot_disconnect` | `fromSlotId toSlotId` | — |
| `vfx_blackboard_read` | `assetPath` | Exposed blackboard properties |
| `vfx_blackboard_add` | `assetPath name typeName` | New property id. `typeName` one of: float, int, bool, Vector2, Vector3, Vector4, Color, Texture2D, Gradient |
| `vfx_blackboard_remove` | `propertyId` | — |
| `vfx_compile` | `assetPath` | Forces a compile, returns `{success, errors[]}` |
| `vfx_errors` | `assetPath` | Current diagnostics without forcing a recompile |

`valueJson` is a **bare JSON literal**, not a wrapper object: `3.5`, `true`,
`"hello"` (string values need their own quotes inside the shell-quoted arg),
or a Unity math struct's `JsonUtility` shape (`{"x":1,"y":2,"z":3}` for a
Vector3). Reading a slot/setting back gives you the same bare-literal shape.

## Workflow

1. **Read before touching anything**: `vfx_graph_read` to see current
   topology and get real node/slot ids — ids from a previous session or a
   previous recompile are stale.
2. **Discover, don't assume**: `vfx_types_list` / `vfx_type_describe` for
   whatever node type you're about to create. VFX Graph's real type catalogue
   and per-block valid-context rules are queryable — querying them is cheaper
   than guessing wrong and debugging a rejected `vfx_node_create`.
3. **Mutate**: `vfx_node_create/delete/move/configure`,
   `vfx_slot_set/connect/disconnect`, `vfx_blackboard_add/remove` as needed.
   One bridge call is one mutation — there's no multi-op transaction, so keep
   changes small and re-read/re-verify between meaningfully different edits.
4. **Compile**: `vfx_compile`. Check `success` and `errors[]` — don't assume
   a mutation that returned successfully also produces a graph that compiles.
5. **Save**: `vfx_graph_save`. Nothing reaches disk until this runs.
6. **Verify**: `vfx_graph_read` again (confirms the in-memory state), and
   optionally check the actual `.vfx` file on disk for the expected
   `m_UIPosition`/setting/value — this is read-only inspection of Unity's own
   serialized output, not something you're writing yourself, so it's a
   legitimate way to double-check a save actually took.

## Test Policy
Per `Docs/project-overview.md`, agents write tests but never run the Unity
test runner — hand the exact test class/method name to the user and ask them
to run it and export XML per `Docs/testing.md` §*Result Files*. Review that
XML yourself before reporting results; don't take a verbal "it passed" as a
substitute when a claim about test results matters (a quick informal
confirmation from the user for a minor, already-covered case is fine — use
judgment).

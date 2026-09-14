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

## Known Capabilities and Limitations
Verified by direct testing against this bridge — not inferred from the API
surface. Check here before planning a change; if what you need is in the
"Not available" list, see *When Blocked* below instead of improvising.

**Available:**
- Create/delete/move a **Block inside an existing Context** (`vfx_node_create`
  with `parentId` set to a real context node id).
- Configure node **settings** (`vfx_node_configure`): attribute name on a
  SetAttribute block, composition mode, blend mode, integration mode, and
  other enum/bool/string settings.
- Read/set **slot values** of type bool, int, float, double, string, enum,
  Vector2/3/4, Color.
- Connect/disconnect **data slots** between existing nodes, including
  fan-out (one output slot linked to several inputs).
- Read/add/remove **blackboard (exposed parameter) properties**
  (`vfx_blackboard_*`) — this goes through a different code path than
  `vfx_node_create` and is unaffected by the top-level-node limitation below.
- `vfx_compile` / `vfx_errors` / `vfx_graph_save` / `vfx_graph_read` to
  verify a change actually took and actually compiles.

**Not available — do not attempt a workaround, see *When Blocked*:**
- **Creating a new top-level node** (an Operator or a graph-level Parameter)
  via `vfx_node_create` with an empty `parentId`. The pipeline server's own
  argument validation rejects an empty/blank `parentId` as "missing," in both
  positional and named-flag form, in both PowerShell and Bash — this is a
  server-side bug, not a shell-quoting issue, and there is no known argument
  spelling that gets past it. Practical effect: you cannot add a new math
  Operator (Multiply, Divide, Modulo, Branch, Compare, a new
  `VFXAttributeParameter` "Get" node, etc.) anywhere in the graph. You *can*
  still reuse and rewire the **existing** top-level operators already in the
  graph (fan out their outputs to new connections).
- **Wiring a new Context into a system's flow** (`m_InputFlowSlot` /
  `m_OutputFlowSlot`). `vfx_node_create` only calls `parent.AddChild(model)`;
  it never touches flow slots, and no other command does either. Practical
  effect: you cannot add a second Output layer (e.g. a separate glow/core
  quad), cannot add a second independent particle system, and cannot insert a
  new stage into the Init → Update → Output chain of an existing system.
- **Reading or writing `AnimationCurve` slot values.** `AgentVfxJson` has no
  case for `AnimationCurve`: `vfx_slot_read` returns an opaque `{}` and
  `vfx_slot_set` throws `NotSupportedException`. Any curve-shaped tuning
  (size-over-life, alpha-over-life, or any other authored curve) is both
  unreadable and unwritable through this bridge.
- **Reading engine-reference-typed slot values** (e.g. a `Texture2D` master
  slot) via `vfx_slot_read` — fails with "JsonUtility.ToJson does not support
  engine types." Writing such a slot is untested; treat it as unverified, not
  confirmed-available, until actually tried.

## When Blocked: Say So, Don't Work Around It
If a requested change needs a capability from the "Not available" list above
(or you hit a new one not yet documented here), stop trying variations. In
particular, never:
- hand-edit the `.vfx` YAML directly,
- patch or extend the bridge's C# source on the fly to unblock yourself,
- or invent an indirect mechanism that technically produces a similar-looking
  result through a path the user didn't ask for.

Instead, **pause** and tell the user plainly what's missing and give them the
exact manual step to do it themselves in the Unity VFX Graph editor UI (e.g.
"this needs a second Output context wired into the same system — drag one in
from the node library and connect its flow input to the Update context's flow
output" or "this needs the size-over-life curve reshaped — open the curve
editor on the `|Set|_Size|Over Life` block and adjust it directly"). Don't
keep going past the blocker on your own judgment about what's "close enough"
or achievable instead — wait. Once the user says the manual change has
landed, resume: re-run `vfx_graph_read` to pick up what they did (ids may
have shifted if it triggered a recompile — see *Confirm Connectivity* above)
and continue the plan from there.

If part of the plan is independent of the blocked step (doesn't depend on it
and won't need redoing once the user's manual change lands), it's fine to
finish that part before pausing rather than leaving it half-done — but still
stop and wait at the blocked step itself rather than substituting a
workaround for it.

## Before Mutating: Plan and Preview First
After reading the current graph (and before issuing any `create`/`delete`/
`configure`/`connect`/`slot_set` call), write out a short plan and share it
with the user before executing:
1. **Each intended change**, in plain terms (new blocks/nodes and what they
   do, settings being changed, values being set, what gets rewired) — not a
   list of bridge calls, a list of what changes about the effect.
2. **What the effect will look like afterward** — describe the resulting
   visual/behavioral result (motion, color, timing, layering) so the user can
   judge it before it's built, not after.
3. Cross-check every part of the plan against *Known Capabilities and
   Limitations* above. If any part isn't achievable, say so as part of the
   plan (per *When Blocked*) instead of silently trimming scope or
   discovering the gap mid-execution.

Only proceed to the Workflow's Mutate step once the user has seen this and
given the go-ahead (or the change is small/obvious enough that this is
clearly unnecessary — use judgment, but default to previewing for anything
that adds/removes nodes or changes what the effect looks like).

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
3. **Plan and preview**: see *Before Mutating: Plan and Preview First* above.
   List the intended changes and the resulting visual outcome, checked
   against *Known Capabilities and Limitations*, before making anything.
4. **Mutate**: `vfx_node_create/delete/move/configure`,
   `vfx_slot_set/connect/disconnect`, `vfx_blackboard_add/remove` as needed.
   One bridge call is one mutation — there's no multi-op transaction, so keep
   changes small and re-read/re-verify between meaningfully different edits.
   If a step turns out to hit something in the "Not available" list, stop per
   *When Blocked* rather than searching for a way around it.
5. **Compile**: `vfx_compile`. Check `success` and `errors[]` — don't assume
   a mutation that returned successfully also produces a graph that compiles.
6. **Save**: `vfx_graph_save`. Nothing reaches disk until this runs.
7. **Verify**: `vfx_graph_read` again (confirms the in-memory state), and
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

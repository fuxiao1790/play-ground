---
name: vfx-agent-full-autonomy
description: Upgrade the AgentVFX bridge from a fixed convenience-command API to a generic reflection executor, removing the "not available" gaps documented in vfx-graph-agent.md
---

# VFX Agent: Full Graph Autonomy

## Summary

`Assets/AgentVFX/` currently exposes VFX Graph editing through a fixed set of
`[CliCommand]`s (`vfx_node_create`, `vfx_slot_connect`, ...). `.agent/vfx-graph-agent.md`
§*Known Capabilities and Limitations* documents four concrete gaps this fixed
surface cannot reach: creating a top-level Operator/Parameter (server-side
`parentId` validation bug), wiring a new Context's flow input/output, reading
or writing `AnimationCurve` slots, and reading engine-reference-typed slots
(e.g. a `Texture2D` master slot). Every one of these is reachable through
`UnityEditor.VFX` internals that the bridge already sits inside
(`Unity.VisualEffectGraph.Editor`, via the existing `.asmref`) — the fixed
command set is the ceiling, not the engine.

This plan adds a generic reflection executor (`vfx_internal_exec`) plus
introspection commands (`vfx_internal_find_types`, `vfx_internal_describe_type`,
`vfx_internal_describe_object`) alongside the existing convenience commands,
per `unity_vfx_agent_incremental_full_autonomy.md` (user-supplied design doc,
referred to below as "the guide"). The fixed commands remain the fast path;
the executor is the fallback for anything they can't express, so the bridge
never again needs a new dedicated command for one more editor operation.

Codegen escape hatch (`vfx_codegen_run`, guide §19-20) is included but ordered
last and marked optional in the task list — the guide itself says to add it
"only after the generic executor works," and it's plausible the executor
alone closes all four known gaps.

## Constraints & Invariants

Sourced from existing code and `.agent/vfx-graph-agent.md`:

1. **Assembly boundary**: no `UnityEditor.VFX` type may appear outside
   `Assets/AgentVFX/InternalAccess/`. Every public member of
   `AgentVfxInternalBridge` (and any new bridge partial) must accept/return
   only strings, primitives, or JSON text.
   Source: [AgentVfxInternalBridge.cs:9-18](../../Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs#L9-L18).
2. **`AgentVFX.Editor` never touches `UnityEditor.VFX` directly** — it only
   calls `AgentVfxInternalBridge` and deals in DTOs.
   Source: [AgentVfxApi.cs:6-8](../../Assets/AgentVFX/Editor/AgentVfxApi.cs#L6-L8).
3. **Path guard**: any asset path used to *open/create/mutate a graph* goes
   through `AgentVfxPathGuard.EnsureAllowed` (`Assets/Vfx/` or
   `Assets/AgentGenerated/` only). This bounds accidental damage to real
   content, not general asset reads.
   Source: [AgentVfxPathGuard.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxPathGuard.cs).
4. **Id map lifetime**: `AgentVfxIdMap` assigns one id per `VFXModel`
   instance for the life of the domain (`ConditionalWeakTable` + dictionary),
   reset only by a domain reload — never by us explicitly clearing it.
   Ids from before a recompile are stale by design.
   Source: [AgentVfxIdMap.cs:8-12](../../Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs#L8-L12).
5. **One bridge type, many partial files**: `AgentVfxInternalBridge` is a
   `static partial class` split one file per concern
   (`AgentVfxNodeOps`, `AgentVfxSlotOps`, `AgentVfxBlackboardOps`,
   `AgentVfxCompileOps`), each documented with which guide section/commands
   it implements.
6. **`[CliCommand]` binding**: parameters bind either from a structured JSON
   `parameters` object or from CLI argv token-by-token
   (`CommandLineBinder`), and an untyped parameter (`object`/`JToken`) keeps
   whatever JSON shape it was given while a `string` parameter always stays a
   string. A command may return any serializable object; existing convenience
   commands return typed DTOs, and `slot_set`/`node_configure` already pass a
   bare-JSON-literal `string valueJson` for anything not a fixed field —
   established precedent for "pass a JSON string when the shape isn't fixed."
   Source: [CommandLineBinder.cs:257-276](../../Library/PackageCache/com.unity.pipeline@ab05e9cd7f74/Runtime/Common/CommandLineBinder.cs#L257-L276).
7. **Newtonsoft.Json is available** to any Editor-only asmdef in this project
   without an explicit reference (precompiled DLL, `overrideReferences: false`
   on `AgentVFX.Editor.asmdef`/InternalAccess `.asmref`) — confirmed via
   `com.unity.pipeline`'s own dependency on `com.unity.nuget.newtonsoft-json`,
   already present in `Library/PackageCache`.
8. **No Undo grouping exists today** in any convenience command (`CreateNode`,
   `DeleteNode`, etc. mutate directly). The guide's transactional requirement
   (§14) is scoped to `vfx_internal_exec` only — see Design Validation below.
9. **Test policy**: agents write EditMode tests, never run them.
   Source: [Docs/project-overview.md](../../Docs/project-overview.md).

## Mechanisms Reused vs. Introduced

**Reused:**
- `AgentVfxInternalBridge` partial-class-per-concern pattern — new ops are
  additional partial files, not a parallel bridge type.
- `AgentVfxIdMap` for any handle that IS a `VFXModel` (node/slot) — the
  executor must hand back the *same* id a convenience command would, so a
  `vfx_internal_exec` result can be fed straight into `vfx_node_move` /
  `vfx_slot_read` and vice versa. This is the single most important reuse
  decision: it's what makes the two command families interoperable instead of
  two disconnected object-reference systems.
- `AgentVfxPathGuard.EnsureAllowed` / `OpenGraph` for `get_graph` — the
  executor opens graphs through the exact same gate as every convenience
  command, no separate path validation.
- `GraphSnapshot`/`NodeSnapshot`/`ConnectionSnapshot` (extended, not
  replaced) for the `vfx_graph_read` extension in guide §16.
- `[CliCommand(..., MainThreadRequired = true)]` convention in
  `AgentVfxCommands.cs`.

**Introduced (justified because no existing mechanism covers them):**
- `AgentVfxHandleMap` — a second, general-purpose weak handle registry for
  non-`VFXModel` objects (arrays, `Type`, boxed structs, `VFXData`, etc.)
  returned mid-execution. `AgentVfxIdMap` is intentionally typed to
  `VFXModel` throughout its call sites (`Resolve<T> where T : VFXModel`);
  widening it to `object` would weaken that contract for every existing
  caller to serve a need only the executor has. New file, same lifetime
  shape (`ConditionalWeakTable`, ids namespaced `obj:N` so they never
  collide with `node:N`/`slot:N`).
- `AgentVfxExecValueCodec` — structured value encode/decode (`$enum`,
  `$type`, `$ref`, `$asset`, arrays, Vector2/3/4, Color, Rect) using
  `Newtonsoft.Json.Linq`. `AgentVfxJson` (existing) is explicitly documented
  as "deliberately not a general JSON<->arbitrary-CLR-type mapper" for bare
  slot/setting literals — a different, narrower problem. Left untouched.
- `AgentVfxExecOps` (bridge partial) — the operation dispatcher
  (`get_graph`, `resolve_type`, `get`, `set`, `call`, `call_static`, `index`,
  `enumerate`, `cast`, `create`, `create_scriptable_object`, `mark_dirty`,
  `save`) plus the transactional wrapper (Undo group, per-op try/catch,
  structured failure).
- `AgentVfxIntrospectionOps` (bridge partial) — `find_types` /
  `describe_type` / `describe_object`.
- Later, optionally: `Assets/AgentVFX/InternalAccess/Generated/` +
  `IAgentVfxGeneratedTask` for the codegen escape hatch.

## Design Validation Against Invariants

- **Boundary (1,2)**: every new public bridge method still returns only
  `string` (JSON text) or DTOs made of primitives/strings. `AgentVfxApi` gets
  thin pass-through methods, same as today; the executor's request/response
  is carried as a JSON `string`, not a typed DTO, because its shape is
  inherently dynamic (bare-JSON-literal precedent, invariant 6) — this keeps
  `AgentVFX.Editor` from needing a `UnityEditor.VFX`-shaped DTO for
  arbitrary reflected members, which would violate invariant 2.
- **Path guard (3)**: `get_graph` reuses `OpenGraph` → guarded. `$asset`
  resolution (loading an existing texture/material to use as a slot value)
  is a **read** of arbitrary already-existing project content, not a graph
  mutation target — explicitly NOT run through `AgentVfxPathGuard`, which
  exists to bound *where AgentVFX creates/writes*, not what it may read as a
  value. Documented as a deliberate scope decision, not an oversight.
- **Id map (4) / reuse (above)**: `AgentVfxExecValueCodec`'s handle-encode
  step checks `is VFXModel` first and defers to `AgentVfxIdMap` before
  falling back to `AgentVfxHandleMap` — so ids stay a single namespace pair,
  not three.
- **Undo scope (8)**: only `vfx_internal_exec` gets an Undo group +
  structured per-operation failure reporting (guide §14). Retrofitting Undo
  grouping onto the existing convenience commands is out of scope for this
  change — flagged under Open Questions, not silently done.
- **Test policy (9)**: every new subtask lists the EditMode test class/method
  names to hand to the user; no task runs them.

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive approach** (chosen for the executor itself):
  - resulting data flow: convenience commands unchanged; a new parallel
    `vfx_internal_exec` path added alongside them, sharing only the id map
    and path guard.
  - new concepts/types introduced: `AgentVfxHandleMap`,
    `AgentVfxExecValueCodec`, `AgentVfxExecOps`, `AgentVfxIntrospectionOps`.
  - copies/translations added: one new JSON encode/decode layer
    (`AgentVfxExecValueCodec`), scoped to the executor only.
  - long-term cost: two ways to express a graph edit (a fixed command, or an
    exec op sequence) for the operations the fixed set already covers. This
    is the guide's own explicit intent (§17: "use convenience commands when
    they fully express the edit; if not, use vfx_internal_exec") — the
    duplication is the point, not an accident, because the two paths serve
    different audiences (common edits vs. anything-the-editor-can-do).
  - **`vfx_graph_read` extension (guide §16)**: refactor, not additive — see
    below.

- **Refactor approach considered for the executor**: replace the fixed
  commands with exec-op equivalents entirely (e.g. reimplement
  `vfx_node_create` as a canned exec-op sequence). Rejected: guide §1
  explicitly keeps the fixed commands as "the fast/common path," and
  collapsing them would remove the one part of the API surface with strict,
  reviewable typed DTOs and validated error messages (e.g.
  `AgentVfxNodeOps.CreateNode`'s compatible-context check at
  [AgentVfxNodeOps.cs:78-85](../../Assets/AgentVFX/InternalAccess/AgentVfxNodeOps.cs#L78-L85)) for no benefit.

- **`vfx_graph_read` extension — refactor, not additive**: extend
  `GraphSnapshot`/`NodeSnapshot`/`BuildGraphSnapshot`/`CollectNodeRecursive`
  in place (new fields: flow connections, child index, settings, model
  runtime type, `VFXData` association) rather than adding a second
  `vfx_graph_read_extended` command or a parallel DTO. One graph reader, one
  source of truth for graph topology — adding a second would immediately
  create the exact "two data paths for the same concept" problem
  `plan-changes.md` calls out.
  - Decision: **refactor** `BuildGraphSnapshot`/`NodeSnapshot`/`ConnectionSnapshot` in place.
  - reason: `vfx_graph_read` is already the only graph-topology reader; a
    second one would fork the source of truth for no reason a real
    constraint demands.

- **Default decision rule applied**: where the guide's suggested shape
  (guide §16's separate `ownership: [...]` array) would introduce a second
  representation of a relationship `NodeSnapshot.parentId` already carries
  (parent/child ownership), fold it into the existing field set instead
  (add `childIndex` next to the existing `parentId`) rather than also adding
  a redundant `ownership[]` array — one representation per relationship.

## Task List

| # | File | Summary |
|---|---|---|
| 001 | [001-internals-visible-to.md](001-internals-visible-to.md) | `InternalsVisibleTo` bridge file + compile smoke test |
| 002 | [002-handle-map.md](002-handle-map.md) | `AgentVfxHandleMap` for non-`VFXModel` handles |
| 003 | [003-value-codec.md](003-value-codec.md) | `AgentVfxExecValueCodec` structured encode/decode |
| 004 | [004-introspection-commands.md](004-introspection-commands.md) | `vfx_internal_find_types` / `describe_type` / `describe_object` |
| 005 | [005-executor-read-ops.md](005-executor-read-ops.md) | `vfx_internal_exec` dispatcher + read-only ops (`get_graph`, `resolve_type`, `get`, `call`, `call_static`, `enumerate`, `index`, `cast`) |
| 006 | [006-executor-mutate-ops.md](006-executor-mutate-ops.md) | Mutation ops (`set`, `create`, `create_scriptable_object`) + transactional wrapper (Undo group, `mark_dirty`, `save`, structured failure) |
| 007 | [007-cli-commands-wiring.md](007-cli-commands-wiring.md) | Wire all new bridge methods into `AgentVfxCommands.cs` / `AgentVfxApi.cs` |
| 008 | [008-extend-graph-read.md](008-extend-graph-read.md) | Extend `vfx_graph_read` snapshot (flow edges, ownership index, nested slots, settings, model type, `VFXData`) |
| 009 | [009-tests.md](009-tests.md) | New EditMode tests (guide §24) |
| 010 | [010-docs.md](010-docs.md) | Update `.agent/vfx-graph-agent.md` (command reference, Known Capabilities/Limitations, workflow) |
| 011 | [011-codegen-escape-hatch.md](011-codegen-escape-hatch.md) | *(optional, last)* `vfx_codegen_run` + `Generated/` + `IAgentVfxGeneratedTask` |

Dependency order: 001 → 002,003 (parallel-safe, both foundational) → 004 →
005 → 006 → 007 → 008 (008 is independent of 002-007 and could run any time
after 001, but is sequenced after so the extended snapshot can itself be
sanity-checked via the new executor) → 009 → 010 → 011.

## Open Questions

1. **Undo symmetry**: should the existing convenience commands
   (`vfx_node_create` etc.) also gain Undo grouping now, for consistency with
   `vfx_internal_exec`? Out of scope here (invariant 8) — flagging for the
   user rather than silently expanding scope.
2. **`vfx_codegen_run` (011)**: the guide frames this as the completeness
   fallback, added only if the executor proves insufficient for some
   operation shape (complex generics, delegates, `ref` structs). Recommend
   building 001-010 first, then checking whether a real blocked scenario from
   `.agent/vfx-graph-agent.md`'s *Known Capabilities and Limitations* still
   needs it before spending the compile/domain-reload cost this adds.

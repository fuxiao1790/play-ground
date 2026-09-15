---
name: vfx-agent-full-autonomy
description: Add bounded reflection and complete graph snapshots to AgentVFX so agents can perform graph edits that fixed convenience commands cannot express.
---

# VFX Agent: Full Graph Autonomy

## Summary

`Assets/AgentVFX/` already edits VFX Graph through typed convenience commands.
Four verified gaps remain in `.agent/vfx-graph-agent.md`: CLI creation of a
top-level node, flow-edge editing, `AnimationCurve` values, and
Unity-engine-object slot values.

This plan keeps convenience commands as the normal path and adds a bounded
reflection executor for graph-local operations that they cannot express. It
also fixes the top-level-node CLI contract directly, replaces the two proposed
JSON conversion paths with one shared value codec, and extends
`vfx_graph_read` into the single complete graph snapshot.

The executor is deliberately not arbitrary Unity Editor code execution. Every
request targets one guarded graph asset. Instance handles are scoped to that
graph or to transient values created for that request. Static reflection is
denied unless a later, reviewed allowlist entry proves necessary.

The previously cited `unity_vfx_agent_incremental_full_autonomy.md` is not in
this repository. This plan is self-contained and does not rely on section
references to that unavailable document.

## Grounded Findings

- Files under `Assets/AgentVFX/InternalAccess/` compile directly into
  `Unity.VisualEffectGraph.Editor` through
  `Unity.VisualEffectGraph.Editor.asmref`. They already access package-internal
  VFX types. Adding `InternalsVisibleTo("AgentVFX.Editor")` is unnecessary and
  would weaken the intended boundary.
- `AgentVfxInternalBridge` crosses into `AgentVFX.Editor` through public methods
  whose signatures contain only strings, primitives, and `void`. Typed DTOs are
  built after JSON crosses that boundary.
- `VFXData` derives from `VFXModel` in VFX Graph 17.4.0. Its snapshot identity
  therefore belongs in `AgentVfxIdMap`, not a general object registry.
- `VFXContext.LinkTo`, `LinkFrom`, `UnlinkTo`, and `UnlinkFrom` are available on
  the installed package model. Flow editing does not require generated code.
- `Undo.SetCurrentGroupName` and `Undo.CollapseUndoOperations` do not record
  graph state. VFX Graph uses a custom graph undo stack from its view-controller
  layer; the model-only bridge cannot honestly promise rollback by grouping
  operations.
- Existing `GraphSnapshot` has no slot collection. Extending `SlotSnapshot`
  alone cannot make nested slots visible through `vfx_graph_read`; the graph DTO
  must gain a canonical `slots` array.
- `com.unity.pipeline` 0.7.0-exp.1 and Newtonsoft.Json 3.0.2 are installed. The
  stale source comment saying Pipeline is absent should be corrected during
  command wiring.

## Constraints and Invariants

1. **Internal API boundary**: direct `UnityEditor.VFX` references stay under
   `Assets/AgentVFX/InternalAccess/`. No `InternalsVisibleTo` grant is added.
   Source: [AgentVfxInternalBridge.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs),
   [Unity.VisualEffectGraph.Editor.asmref](../../Assets/AgentVFX/InternalAccess/Unity.VisualEffectGraph.Editor.asmref).
2. **DTO boundary**: `AgentVFX.Editor` and `AgentVFX.Commands` receive only JSON,
   strings, primitives, or DTOs without VFX-internal types.
   Source: [AgentVfxApi.cs](../../Assets/AgentVFX/Editor/AgentVfxApi.cs),
   [AgentVfxDtos.cs](../../Assets/AgentVFX/Editor/AgentVfxDtos.cs).
3. **Single guarded graph**: each executor request contains exactly one `asset`
   path, opened through `AgentVfxInternalBridge.OpenGraph`, which calls
   `AgentVfxPathGuard.EnsureAllowed`. Any resolved `VFXModel` must belong to that
   graph; cross-graph handles fail before invocation. Source:
   [AgentVfxPathGuard.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxPathGuard.cs).
4. **Asset references are reads**: `$asset` may load an existing project asset
   as a value. It must be an `Assets/...` path, must exist, and must be assignable
   to the expected `UnityEngine.Object` type. It never authorizes writing that
   asset.
5. **Stable VFX identity**: every `VFXModel` uses `AgentVfxIdMap`: visible graph
   nodes use `node:N`, slots use `slot:N`, and `VFXData` uses `data:N`.
   Non-model transient reference objects use a separate weak registry with
   `obj:N` ids. All ids remain domain-lifetime only. Source:
   [AgentVfxIdMap.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs),
   [VFXData.cs](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Data/VFXData.cs).
6. **One value codec**: convenience commands and executor operations share one
   JSON-to-CLR codec. Bare primitive/struct JSON remains backward compatible;
   tagged values add curves, assets, enums, types, persistent refs, and local
   refs. Source: [AgentVfxJson.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxJson.cs),
   [AgentVfxSlotOps.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxSlotOps.cs).
7. **Bounded reflection**: type discovery is limited to the VFX editor assembly
   plus explicitly supported Unity value/asset types. Reflection infrastructure,
   filesystem/process APIs, `AssetDatabase` mutation APIs, delegates, pointers,
   open generics, and arbitrary static calls are outside the command surface.
8. **Main thread**: all CLI commands remain `MainThreadRequired = true`.
   Reflection, `ScriptableObject`, `AssetDatabase`, graph mutation, compile, and
   save work stays on Unity's main thread. Source:
   [AgentVfxCommands.cs](../../Assets/AgentVFX/Commands/AgentVfxCommands.cs).
9. **No false atomicity**: an executor batch executes sequentially. Failure stops
   at the failing operation but may leave earlier mutations in memory. Saving is
   an end-of-request action performed only after every operation succeeds.
10. **Read-plan-mutate-compile-save-verify workflow** remains mandatory. Generic
    reflection is a fallback, not permission to skip preview, compile, or
    post-save verification. Source: [vfx-graph-agent.md](../vfx-graph-agent.md).
11. **Test ownership**: agents write tests but do not run Unity tests. User runs
    named EditMode tests and exports `Logs/TestResults-EditMode-VfxAgent.xml`.
    Results are not called passing until that XML is reviewed.
    Source: [project-overview.md](../../Docs/project-overview.md),
    [testing.md](../../Docs/testing.md).

## Wire Contract

One command argument carries one structured JSON request. CLI layer accepts it
as `JToken`; API converts it to compact JSON text before crossing internal
assembly boundary:

```json
{
  "asset": "Assets/AgentGenerated/Example.vfx",
  "saveOnSuccess": false,
  "operations": [
    { "op": "get_graph", "as": "graph" },
    { "op": "get", "target": { "$local": "graph" }, "member": "children", "as": "children" },
    { "op": "enumerate", "target": { "$local": "children" }, "as": "nodes" }
  ]
}
```

- `as` defines a request-local name. `{"$local":"name"}` can appear anywhere
  an operand or argument is accepted.
- `{"$ref":"node:4"}`, `{"$ref":"slot:8"}`, `{"$ref":"data:1"}`, and
  `{"$ref":"obj:2"}` are domain-lifetime handles returned by earlier calls.
- `{"$asset":{"path":"Assets/...","type":"UnityEngine.Texture2D"}}`
  represents an existing engine object.
- `saveOnSuccess` defaults to `false`. There is no mid-sequence `save` operation.

Success returns encoded results in operation order plus `saved`. Failure returns
`failureStage` (`validation`, `operation`, `postprocess`, or `save`), nullable
`failedOperation`, completed results, a structured error, `saved:false`, and
`mayHaveMutated`. Here `saved:false` means save was not confirmed; a save-stage
exception still requires disk/state inspection. Error responses never imply
rollback.

## Mechanisms Reused

- `AgentVfxInternalBridge` partial files and `OpenGraph`.
- `AgentVfxPathGuard` for every executor graph path.
- `AgentVfxIdMap` for all `VFXModel` identities.
- `VFXLibrary` descriptors and existing `CreateNode` logic for authored node
  creation; executor creation must not invent a parallel node catalogue.
- Existing snapshot/DTO types, extended in place.
- Existing compile and save bridge methods after a successful mutation batch.
- Existing `[CliCommand(..., MainThreadRequired = true)]` convention.

## Mechanisms Introduced

- `AgentVfxReflectionPolicy`: central type/member/target validation and graph
  scope checks. This prevents each operation from implementing a different
  safety interpretation.
- `AgentVfxHandleMap`: weak identities only for non-`VFXModel` transient
  references. Entries carry graph scope when derived from a graph request.
- `AgentVfxValueCodec`: replaces duplicated primitive conversion logic and adds
  `AnimationCurve`, `Gradient`, engine assets, arrays/lists, tagged enums/types,
  local aliases, and handles.
- `AgentVfxIntrospectionOps`: bounded type/object descriptions.
- `AgentVfxExecOps`: schema validation, local bindings, operation dispatch,
  structured results, failure reporting, final invalidation, and
  save-on-success.

## Design Validation

- Boundary holds because internal VFX types never appear in a public bridge
  signature or outside `InternalAccess/`; task 001 removes the proposed friend
  assembly rather than adding one.
- Path guard cannot be bypassed through a durable model handle because every
  executor call opens its request graph and verifies model ownership before
  access. Default-denied static calls prevent arbitrary asset API entry points.
- Identity stays interoperable: executor encoding checks `VFXModel` before
  `UnityEngine.Object` or general reference types, then delegates to
  `AgentVfxIdMap`.
- Curve and engine-reference values use the same codec in `vfx_slot_read/set`
  and executor operations. There is one serialized value contract.
- Partial failure is explicit. Schema and statically resolvable members are
  preflighted before mutation; runtime-dependent failures still report
  `mayHaveMutated:true`. Nothing saves after failure.
- Complete graph read uses one nodes array, one slots array, one data-edge array,
  and one flow-edge array. Ownership is represented once by `parentId` plus
  `childIndex`; slot ownership is represented once by `nodeId`/`parentSlotId`.

## Additive vs. Refactor Comparison

### Minimal additive approach

- Keep `AgentVfxJson`; add separate executor codec.
- Add reflection without graph scoping or central policy.
- Add a second extended graph reader.
- Group Undo calls and label the batch transactional.
- Cost: duplicate wire formats, path-guard bypasses, divergent topology, and an
  untrue rollback guarantee.

### Refactor approach

- Replace value conversion internals with one codec while preserving old bare
  JSON inputs.
- Extend `vfx_graph_read` in place.
- Centralize reflection policy and bind every request to one graph.
- Treat save as commit-after-success and failures as explicitly partial.
- Cost: touches existing slot/settings serialization and DTOs, requiring broader
  compatibility tests.

### Decision

Choose refactor. One value contract and one graph snapshot remove more
long-term complexity than they add. Reflection remains additive only where no
existing typed API exists.

## Task List

| # | File | Summary |
|---|---|---|
| 001 | [001-executor-contract-policy.md](001-executor-contract-policy.md) | Preserve assembly boundary; define executor schema and reflection policy |
| 002 | [002-handle-map.md](002-handle-map.md) | Add scoped weak handles for non-`VFXModel` objects |
| 003 | [003-value-codec.md](003-value-codec.md) | Refactor to one value codec; add curves and engine references |
| 004 | [004-introspection-commands.md](004-introspection-commands.md) | Add bounded type/object introspection |
| 005 | [005-executor-read-ops.md](005-executor-read-ops.md) | Add request parsing, preflight, local aliases, and read operations |
| 006 | [006-executor-mutate-ops.md](006-executor-mutate-ops.md) | Add graph-scoped mutations, explicit partial-failure semantics, and save-on-success |
| 007 | [007-cli-commands-wiring.md](007-cli-commands-wiring.md) | Wire commands and fix top-level-node CLI sentinel |
| 008 | [008-extend-graph-read.md](008-extend-graph-read.md) | Make `vfx_graph_read` canonical for nodes, nested slots, data edges, and flow edges |
| 009 | [009-tests.md](009-tests.md) | Add public-surface EditMode and command-contract tests |
| 010 | [010-docs.md](010-docs.md) | Update operating guide after user-provided XML is reviewed |
| 011 | [011-codegen-decision.md](011-codegen-decision.md) | Record rejection of runtime codegen; require a new reviewed plan if a proven gap remains |

Dependency order: 001 -> 002 -> 003 -> 004 -> 005 -> 006. Task 008 may start
after 003. Task 007 follows 004-006. Task 009 follows 001-008. Task 010 is
finalized only after user test XML is reviewed. Task 011 is a decision record,
not an implementation phase.

## Completion Barrier

Implementation can be code-complete after task 009, but capability claims stay
provisional until user runs listed EditMode tests and provides
`Logs/TestResults-EditMode-VfxAgent.xml`. Task 010 then moves only proven gaps
from “Not available” to “Available.” Any failed or unexpressible case gets a
new narrow plan; it does not activate generated code automatically.

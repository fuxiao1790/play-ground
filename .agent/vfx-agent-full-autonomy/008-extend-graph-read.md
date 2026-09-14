# 008 — Extend `vfx_graph_read`

## Goal
Guide §16: give `vfx_graph_read` enough topology detail that the agent can
plan an exec-op edit without guessing at flow wiring, ownership order,
nested slot structure, or per-node settings. This is a **refactor** of the
existing snapshot types, not a new parallel reader — see index.md's
Minimal/Additive vs. Refactor Comparison for why.

## Dependencies
001 only (does not depend on the executor — could be built any time after
001; sequenced here so it can be sanity-checked against the new
introspection/exec commands once those exist, per index.md's task-order
note).

## Files to change
- `Assets/AgentVFX/InternalAccess/AgentVfxSnapshots.cs` — extend
  `NodeSnapshot` in place:
  - `childIndex` (int) — this node's position among its parent's children.
    Folds guide §16's separate `ownership: [{parent, child, index}]` array
    into the existing `parentId` field's neighborhood (index.md's "default
    decision rule": one representation per relationship, not two).
  - `runtimeType` (string) — `model.GetType().FullName`, distinct from the
    existing `typeId` (which is coarser — see
    [AgentVfxNodeOps.cs:134-137](../../Assets/AgentVFX/InternalAccess/AgentVfxNodeOps.cs#L134-L137)'s
    own comment on why `typeId` can't recover catalogue provenance). Callers
    that want to `vfx_internal_describe_type`/`vfx_internal_exec` against a
    node need the *exact* runtime type, not the coarse kind:fullname pairing
    `typeId` already provides.
  - `settings` (new `AgentVfxSettingSnapshot[]`: `name`, `valueJson`) — every
    `[VFXSetting]`-attributed field's current value, using the existing
    `GetSettingNames` helper pattern from
    [AgentVfxNodeOps.cs:218-236](../../Assets/AgentVFX/InternalAccess/AgentVfxNodeOps.cs#L218-L236)
    (reuse that method rather than re-deriving setting names) plus
    `AgentVfxJson.ToJson` for each value (reuse — these are the same
    bool/int/float/enum-shaped values `vfx_node_configure` already writes).
  - `dataId` (string, nullable) — for a `VFXContext`, its `VFXData`
    association (guide §16), encoded as an `AgentVfxHandleMap`/`AgentVfxIdMap`
    handle (VFXData is not a VFXModel — confirm at implementation time via
    `vfx_internal_describe_type` which map applies; likely
    `AgentVfxHandleMap` since `VFXData` sits outside the `VFXModel`
    hierarchy). Two contexts sharing the same `dataId` are the same particle
    system — this is how the agent tells "two contexts, one system" from
    "two independent systems" (a gap called out directly in
    `.agent/vfx-graph-agent.md`'s Not-Available list).
  - Extend `SlotSnapshot`: `path` (string — dotted path from the master
    slot, e.g. `"position.x"`), `direction` ("Input"/"Output", clarifying
    vs. the existing `isOutput` bool — keep both only if `direction` carries
    strictly more information than the bool already does; otherwise this is
    scope creep and should be dropped at implementation time), `space`
    (coordinate space string, when the slot type has one).
  - New top-level `GraphSnapshot` field: `flowConnections` (new
    `FlowConnectionSnapshot[]`: `fromContext`, `fromSlotIndex`,
    `toContext`, `toSlotIndex`) — separate array from the existing
    `connections` (data-slot links), per guide §16's explicit
    "data edge != flow edge != ownership relation" — these are genuinely
    different relationship kinds (not the "fold redundant representations"
    case the `childIndex` decision above was), so a distinct array is
    correct here, not scope creep.
- `Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs` — extend
  `BuildGraphSnapshot`/`CollectNodeRecursive` to populate the new fields,
  and add flow-connection collection (walk each `VFXContext.outputFlowSlot`,
  per
  [VFXContext.cs:571-577](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Models/Contexts/VFXContext.cs#L571-L577)).
- `Assets/AgentVFX/Editor/AgentVfxDtos.cs` — mirror every new field on
  `AgentVfxNodeDto`/`AgentVfxSlotDto`/`AgentVfxGraphDto` (field names must
  match exactly — `JsonUtility` matches by name only, per the existing
  header comment on this file).

## Acceptance Criteria
- Reading the existing `CanReadRealProductionGraph` fixture
  (`Assets/AgentGenerated/TestFixtures/MagicBoltTrail.vfx`) now returns
  non-empty `flowConnections` (that fixture has "multiple wired-together
  contexts" per
  [AgentVfxCompatibilityTests.cs:42-44](../../Assets/Tests/EditMode/AgentVfxCompatibilityTests.cs#L42-L44)).
- Every `Block`'s `childIndex` matches its actual position in its parent
  context's block list.
- `runtimeType` differs from `typeId` in a way that's actually more precise
  (assert the exact string for at least one known type during test-writing).
- All existing `AgentVfxCompatibilityTests` still pass unmodified (this task
  changes DTO shape additively — no existing field removed/renamed).

## Scope
Medium. Touches four existing files but each change is additive to an
existing struct/method, not a rewrite.

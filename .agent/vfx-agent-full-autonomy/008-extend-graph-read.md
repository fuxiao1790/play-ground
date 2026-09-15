# 008 - Canonical Complete Graph Snapshot

## Goal

Extend `vfx_graph_read` in place so it fully describes ownership, nested data
slots, data edges, flow edges, settings, runtime types, and VFXData sharing.

## Dependencies

003. It may be implemented in parallel with 004-007.

## Files

- Update `AgentVfxSnapshots.cs`, `AgentVfxDtos.cs`,
  `AgentVfxInternalBridge.cs`, `AgentVfxNodeOps.cs`, and `AgentVfxSlotOps.cs`.

## Canonical Shape

- Extend each node with:
  - `runtimeType` (`model.GetType().FullName`)
  - `childIndex` within parent (`-1` for graph-level nodes)
  - `settings[]` (`name`, `valueJson`) using shared value codec
  - `dataId` for context `GetData()`, using `AgentVfxIdMap` `data:N`; null when
    absent. Equal ids mean shared VFX system data.
- Add top-level `slots[]`. Include every master input/output slot and all
  descendants recursively. Each slot carries:
  - existing id/node/name/type/value/link fields
  - `parentSlotId` (null for master)
  - `childIndex`
  - dotted `path` from master
  - `space` when spaceable
- Keep node `inputSlotIds`/`outputSlotIds` as direct master-slot indexes for
  compatibility. Do not add redundant direction string; existing `isOutput`
  owns that fact.
- Keep `connections[]` as data-slot edges.
- Add `flowConnections[]` with `fromContext`, `fromSlotIndex`, `toContext`, and
  `toSlotIndex`, derived from one direction only to avoid duplicates.

Ownership remains one representation: `parentId + childIndex` for nodes and
`nodeId + parentSlotId + childIndex` for slots. Do not add parallel ownership
edge arrays.

## Implementation Notes

- Refactor existing slot traversal so graph read and `vfx_slot_read` call the
  same `BuildSlotSnapshot` helper.
- `GetSettingNames` becomes reusable internal helper; do not reimplement
  attribute scanning.
- Use shared codec for settings/slot values so curves and engine objects never
  degrade to `{}`.
- Preserve every existing field and meaning; additions are backward compatible.

## Acceptance Criteria

- Real scratch fixture returns non-empty nodes, slots, data connections, and
  flow connections.
- Every nested slot has resolvable ancestry/path and appears exactly once.
- Every block's `childIndex` matches parent context order.
- Contexts sharing a particle system share one `data:N` id.
- Existing DTO consumers compile without field removal/rename.

## Scope

Medium-large.

# 006 — Update docs and context

## Changes
- [Docs/contracts/render-batch-data.md](../../Docs/contracts/render-batch-data.md):
  - `CombatRenderBatchId` is now a plain `IComponentData` int, not shared data.
  - State explicitly that it no longer partitions chunks or the spawn pool; it is
    a per-entity copy of `RenderTypeId` used only to select GPU resources at
    submit.
  - Update the "Ordering"/"Fields" sections: submission scatters by the int and
    no longer uses `ToComponentDataArray` or a shared filter.
- [.agent/rendering-rework/context.md](./context.md): update the
  "Rework-relevant state" section — the shared-component coupling is removed; note
  the deferred follow-ups (parallel compaction, dropping `CombatRenderElement`,
  removing degenerate projectile counting-sort).
- If any `Docs/reference/simulation/*` page describes batch id as a shared
  component / chunk partition key, correct it (grep for `CombatRenderBatchId` and
  `SharedComponent`).

## Acceptance criteria
- No doc describes `CombatRenderBatchId` as `ISharedComponentData` or as a spawn
  pool partition.

## Depends on
002, 003, 004.

# Task Execution Packet

## Task
006-docs-update.md

## Goal
Update render-batch docs and rework context so they describe `CombatRenderBatchId` as plain component data and no longer as a spawn pool partition.

## Files Allowed To Modify
- `Docs/contracts/render-batch-data.md`
- `Docs/contracts/spawn-events-and-commands.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/project-aoe-system-common.md`
- `Docs/layers/presentation-and-feedback.md`
- `.agent/rendering-rework/context.md`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/006-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/006-docs-update.md`
- `Docs/contracts/render-batch-data.md`
- `.agent/rendering-rework/context.md`
- Grep results for render shared component wording.

## Behavior To Preserve
- Docs still reflect registry-owned GPU resources and presentation-owned draw submission.

## Behavior To Change
- Docs state `CombatRenderBatchId` is plain `IComponentData`.
- Docs state spawn pooling is not partitioned by batch id.
- Docs state render submission scatters by batch id and no longer uses shared filters or `ToComponentDataArray`.

## Relevant Global Context
- Parallel render compaction, dropping `CombatRenderElement`, and removing degenerate projectile counting-sort are deferred.

## Dependencies Confirmed
- Tasks 002-004 completed code changes for spawn and render.

## Step-By-Step Instructions
- Update render-batch contract fields, guarantees, restrictions, and ordering.
- Update rework context current-state section.
- Correct other current docs that refer to render shared components or batch-id keyed reuse.

## Acceptance Criteria
- No current docs describe `CombatRenderBatchId` as `ISharedComponentData` or as a spawn pool partition.

## Validation Required
- Grep docs for `CombatRenderBatchId`, `ISharedComponentData`, and render shared component wording.

## Hard Boundaries
- Do not rewrite unrelated architecture docs.
- Do not modify task specs retroactively.

# Task Execution Packet

## Task
008-docs-and-verification.md

## Goal
Update docs and record required verification.

## Files Allowed To Modify
- `Docs/reference/simulation/combat-render-system.md`
- `Docs/contracts/render-batch-data.md`
- `.agent/combat-render-sorting-layer/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Final changed code.

## Behavior To Preserve
- Docs remain descriptive and checked against code.

## Behavior To Change
- Docs describe baked mesh + MeshRenderer + Sorting Layer path.

## Relevant Global Context
- Old indirect feature is gone.
- CombatSprites layer is a single atomic renderer tier.

## Dependencies Confirmed
- 001 through 007 complete.

## Step-By-Step Instructions
- Update render system summary, flow, key types, constraints, performance notes.
- Update render batch contract fields, guarantees, and ordering.
- Run available validation and record manual verification gaps.

## Acceptance Criteria
- Docs no longer claim the old indirect RendererFeature path is current.

## Validation Required
- Search for stale old-path terms where relevant.
- Compile/build if possible.

## Hard Boundaries
- Do not update unrelated docs.

# Task Execution Packet

## Task

004-resolve-core-and-systems.md

## Goal

Implement updated targeted resolve: occupied-AOE-cell shape-overlap/deduped nearest-rank selection, precomputed gate slots, per-link hit/VFX, single expiry, interval restarts, exact lanes.

## Files Allowed To Modify

- `Assets/Scripts/System/Targeted/TargetedResolveCore.cs` (new)
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs` (new)
- Existing/new focused EditMode targeted resolve tests.
- Imports directly required by these files.

## Files Allowed To Create

- The two resolve files and focused EditMode test fixture only.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `AoeCollisionCore`, impact/lingering AOE collision systems, TargetSpatialHash types, `CombatDeathUtility`, VFX lane, combat hit lane, target proxy/broadphase tests, task-003 targeted contracts/lanes.

## Behavior To Preserve

- Existing AOE/projectile collisions, hit aggregation, pool lifecycle, and lane ownership.
- No managed/random/component-lookup work in resolve job.

## Behavior To Change

- Targeted entities resolve links through spatial snapshot only. Single chains kill when ended; lingering chains restart on tick while preserving last target exclusion.

## Relevant Global Context

- Query requires TargetedTag and Active; single excludes LingeringTargetedTag, lingering includes it; both exclude armed entities.
- Use `TargetedResolveConfig`, `TargetedVfxIds`, visual-only `TargetedVfxSizeComponent`, `VfxTimingData`, and command-initialized state. No global radius cap; scan occupied AOE cells using compiled radii.
- Combines broadphase BuildHandle and publishes ConsumerHandle; combines resulting handle into hit/VFX lane ProducerHandle. Lanes retrieved direct RW.
- `MaxChainTargets = 32` bounds rank/walk. No liveness lookup, global radius clamp, tracking-cell centre query, or new spatial container.

## Dependencies Confirmed

- Tasks 002 and 003 complete: targeted archetypes/contracts/spawn lanes exist.

## Step-By-Step Instructions

1. Copy AOE collision scheduling/lane dependency topology with two thin systems and static Burst core.
2. Implement `availableHits` gate calculation, `currentPosition` from walk state, immediate-previous exclusion, per-landed-link gate advance, falloff, VFX, and render mirror only after a link.
3. Implement AoeOccupiedCells/AoeCellSize circle scan, circle-vs-shape overlap, target-index dedupe, rank-offset/wrap, centre-distance/index tie break, faction/exclusion filtering.
4. Implement single expiry and lingering tick restart semantics exactly as task.
5. Add listed focused EditMode coverage for selection, timing/catch-up, rank/wrap, restart/exclusion, VFX topology, render state, arming, and cap.

## Acceptance Criteria

- Every task-004 scenario is represented in focused tests and code adheres to exact chain/state timing.

## Validation Required

- Focused Unity EditMode tests if editor unlocks; otherwise state limitation.
- Static checks for no ComponentLookup/Random, no tracking-cells/radius-clamp path, occupied-cell shape-overlap/dedupe, target tags, lane/spatial handles, bounded rank/walk, and correct ordering.

## Hard Boundaries

- Do not change task-003 lanes, lifetime/arming/pool cleanup implementation, render systems, root/compiler/authoring, or VFX dispatch infrastructure.
- Do not add a spatial structure or managed dependencies.

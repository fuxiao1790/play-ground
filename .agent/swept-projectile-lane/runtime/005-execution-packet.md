# Task Execution Packet

## Task

005-spawn-apply-lane-split.md

## Goal

Separate discrete/swept pooled apply lanes with shared common materialization; swept archetype has sweep state but no tracking component.

## Files Allowed To Modify

- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`

## Files Allowed To Create

- `Assets/Scripts/System/Projectiles/SweptProjectileSpawnApplySystem.cs`

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Existing projectile apply system; AOE apply utility/lane; spawn expansion singleton; pool top-up/stat components; tracking system.

## Behavior To Preserve

- Discrete application/pooling/tracking behavior; common materialization fields; existing tracking system unmodified.

## Behavior To Change

- Discrete dead-slot query excludes swept tag; swept commands receive distinct archetype/pool and seed origin.

## Relevant Global Context

- Shared writer utility includes data common to both lanes only. Tracking field/enabled mask remains discrete-only.
- Both lanes use one expansion pending handle; each counts reuse/ECB spawns in existing stats.
- New archetype contains both domain tag and swept tag/component, but omits tracking structurally. No extra tracking filters.
- Origin always set to command position on reuse/cold create.

## Dependencies Confirmed

- `SweptCommands`, `SweptProjectileTag`, and `ProjectileSweepComponent` exist.
- Existing discrete apply system has complete archetype/pool/job/materialization pattern.

## Step-By-Step Instructions

1. Extract only common materialization and listed state helpers to internal static utility; leave tracking writes/mask in discrete job.
2. Add `.WithNone<SweptProjectileTag>()` to discrete disabled-slot query.
3. Add swept apply system copied from discrete pattern: archetype replace tracking with tag+sweep component; tag-inclusive disabled query; consume swept commands; own profiler metrics; existing stat counters.
4. Swept job uses shared writer then writes origin; declares no tracking handle.
5. Preserve lifecycle/cleanup/query ownership.

## Acceptance Criteria

- Distinct pools, discrete query excludes swept, swept lacks tracking.
- Reuse materialization origin correct; stats counted both paths.
- Tracking systems untouched.

## Validation Required

- Static structural query/archetype/handle checks and diff check. Compile baseline may remain blocked.

## Hard Boundaries

- No movement/collision work from later tasks; no edits to tracking systems.

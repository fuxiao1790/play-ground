# Task Execution Packet

## Task

005-lifetime-arming-pool-cleanup.md

## Goal

Extend existing lifetime, arming, cleanup, and stats systems for both targeted pools, using targeted visual components on expiry.

## Files Allowed To Modify

- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatArmingSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`
- Relevant combat stats/debug display files.
- Directly related EditMode tests.

## Files Allowed To Create

- Focused EditMode test files only.

## Files Allowed To Delete

- None.

## Relevant Context

- Targeted single/lingering archetypes already exist with TargetedTag/LingeringTargetedTag, `Active`, `ArmingTag`, lifetime, VFX ids/sizes/timing.
- Targeted has no collision active tag; lifetime uses projectile-shaped kill overload.
- Stats are scene-wide pool-cleanup correctness data, not presentation-only telemetry.

## Instructions

1. Mirror explicit per-domain lifetime and arming jobs; target queries require TargetedTag and lifetime pauses during arming.
2. Expiry emits TargetedVfxIds.ExpireId at current kinematic position with visual EffectSize/timing, then disables using non-collision kill overload.
3. Add two independent targeted cleanup queries/pools.
4. Add targeted spawn/link counters to stats and stable display snapshot; consume existing task-003 spawn counts correctly.
5. Add task acceptance tests for interval duration/walk count, arming pause, single fail-safe expiry, independent trimming, mixed-scene count conservation, and display.

## Validation

- Focused EditMode tests if available; otherwise exact editor-lock reason.
- Static checks: tagged domain queries, no collision tag, targeted expiry visual sources, six cleanup pools, stats display copy.

## Hard Boundaries

- Do not change resolver/spawn lane semantics, authoring/compiler/root integrations, or Unity assets/YAML.

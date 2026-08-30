---
name: remove-gate-from-spawn-materialization
description: Remove AoeHitGateComponent from both AOE archetypes and their spawn-apply jobs, delete HitGateFor, and drop it from ImpactAoeCollisionSystem's query.
---

# 002 - Remove the Gate From Spawn Materialization

## Depends On

[001-remove-gate-from-lingering-collision.md](001-remove-gate-from-lingering-collision.md)
should land first so nothing reads the component by the time it's stripped
from spawn-time data.

## Changes

### [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)

Impact side:
1. [Line 47](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L47) — remove `typeof(AoeHitGateComponent),` from `_impactArchetype`.
2. [Line 125](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L125) — remove `HitGateHandle = GetComponentTypeHandle<AoeHitGateComponent>(false),`.
3. [Line 169](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L169) — remove `public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;`.
4. [Line 203](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L203) — remove the `NativeArray<AoeHitGateComponent> hitGates = ...` line and whatever downstream call passes `hitGates` into the shared per-slot materialization helper for this job (trace the local variable's uses within this `Execute`/job body).

Lingering side (mirror of the above):
5. [Line 308](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L308) — remove `typeof(AoeHitGateComponent),` from `_lingeringArchetype`.
6. [Line 390](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L390) — remove the handle assignment.
7. [Line 439](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L439) — remove the handle field.
8. [Line 479](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L479) — remove the `hitGates` array fetch and its downstream use.

Shared materialization helper:
9. [Lines 581, 597](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L581) — the shared per-slot apply function currently takes `NativeArray<AoeHitGateComponent> hitGates` as a parameter and does `hitGates[index] = HitGateFor(cfg);` at line 597. Remove the parameter and this line (update both call sites from steps 4 and 8 to stop passing it).
10. [Lines 691-692](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L691-L692) — delete the `HitGateFor` static method entirely.

Do not touch `PulseVfxFor` ([line 654](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L654)) or `VfxTimingFor`
([line 660](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L660)) in this task — they're renamed in task 004, not touched here.

### [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs)

Remove `.WithAll<AoeHitGateComponent>()` from the query builder in `OnCreate`
(~[line 38](../../Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs#L38)).
This job never had a `HitGateHandle` or read the component — this is purely
dropping a now-pointless query requirement.

## Acceptance Criteria

- Neither `_impactArchetype` nor `_lingeringArchetype` include `AoeHitGateComponent`.
- `HitGateFor` no longer exists anywhere in the file.
- Both spawn-apply jobs still compile and Burst-compile with the `hitGates`
  parameter/array removed from the shared materialization helper's signature.
- `ImpactAoeCollisionSystem`'s query no longer requires `AoeHitGateComponent`.
- `AoeHitGateComponent` itself still exists as a type after this task (deleted
  in task 003) — this task only removes it from archetypes/queries/materialization.

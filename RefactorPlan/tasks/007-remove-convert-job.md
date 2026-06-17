# Task 007: Remove the dead generic convert job and pending-spawn type

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files. Pure cleanup of now-dead code.

## Goal
Delete `CombatSpawnConvertJob` and the generic `CombatPendingSpawn` transient now that both collision systems emit typed `ProjectileSpawnEvent` / `AoeSpawnEvent` directly.

## Required Reading
- `../context/002-target-architecture.md` (§2 Collision)
- `../context/005-decision-log.md` (D-CONVERT-RELOCATE)

## Design Decisions Already Made
- `CombatSpawnConvertJob` is deleted (its logic was relocated to `ProjectileSpawnPipeline` / `AoeSpawnPipeline` in Tasks 002/005).
- `CombatPendingSpawn` is deleted. The consequence snapshot structs it referenced (`ProjectileImpactAoeSnapshot`, `ProjectileImpactProjectileSnapshot`, `AoeProjectileBurstSnapshot`) STAY — they are carried by the typed events and the hit payloads.

## Why This Task Exists
Design §7.2: collision is the final producer of typed events; the generic re-interpretation pass is removed. After Tasks 004 + 006 nothing schedules the convert job or writes `CombatPendingSpawn`.

## Current Code References
```
Assets/Scripts/System/Common/CombatSpawnConvertJob.cs  — DELETE (no longer scheduled after T004/T006).
Assets/Scripts/System/Common/CombatHitElement.cs
- struct CombatPendingSpawn (lines ~228-242). DELETE this struct.
- KEEP: ProjectileImpactAoeSnapshot, ProjectileImpactProjectileSnapshot, AoeProjectileBurstSnapshot,
  CombatPendingDamage (renamed in Task 008), CombatDamageElement, CombatStatusEffectSnapshot.
```

## Files To Modify
- `System/Common/CombatHitElement.cs` — remove `struct CombatPendingSpawn`. Keep everything else.
- Any pipeline helper that still takes `CombatPendingSpawn` as a parameter (from Tasks 002/005) — if so, change the signature to primitive args (faction, sourceId, typeId, targetId, position, targetPosition, snapshot). The collision callers (Task 004/006) already have these values locally.

## Files To Delete
- `System/Common/CombatSpawnConvertJob.cs`

## Required Changes
1. Confirm (grep) there are zero remaining references to `CombatSpawnConvertJob` and `CombatPendingSpawn` in non-test and test code.
2. If `ProjectileSpawnPipeline.BuildImpactProjectileEvent`/`BuildBurstEvent`/`AoeSpawnPipeline.BuildImpactAoeEvent` still accept `CombatPendingSpawn`, refactor them to primitive parameters and update the collision callers.
3. Delete `CombatSpawnConvertJob.cs` (+ `.meta`).
4. Delete `CombatPendingSpawn`.
5. Recompile; run.

## Behavior Preservation Requirements
- No behavior change — this only removes code that is no longer executed.

## Intentional Behavior Changes
None. This task should be behavior-preserving.

## Out of Scope
- The damage rename (Task 008).

## Dependencies
- Task 004 (projectile cutover) AND Task 006 (AoE cutover) — both must be done so nothing references the deleted symbols.

## Follow-Up Tasks
- Task 008 (damage rename + bridge), Task 009 (ordering audit).

## Implementation Constraints
- Burst: if you change helper signatures, keep them Burst-callable (primitives + blittable snapshot structs).
- Do not remove the consequence snapshot structs — they are still used by events and hit payloads.

## Step-by-Step Implementation Plan
```
1. Grep for CombatSpawnConvertJob and CombatPendingSpawn — expect only the definitions + (maybe) helper signatures.
2. Refactor helper signatures off CombatPendingSpawn if needed; update collision callers.
3. Delete CombatSpawnConvertJob.cs (+ meta).
4. Remove the CombatPendingSpawn struct from CombatHitElement.cs.
5. Recompile; run full suite.
```

## Acceptance Criteria
```
- [ ] CombatSpawnConvertJob.cs is deleted.
- [ ] CombatPendingSpawn no longer exists.
- [ ] Consequence snapshot structs (ImpactAoe/ImpactProjectile/Burst) still exist and compile.
- [ ] Zero references to the deleted symbols anywhere.
- [ ] Repo compiles; all tests pass; behavior unchanged.
```

## Validation
- Compile + full test suite. Manual smoke of `Main.unity` (impact projectiles, impact AoEs, bursts still work) to confirm nothing was relying on the convert job.

## Risk Level
Low — removal of code already bypassed; the cutover tasks already exercised the replacement paths.

## Failure Modes
- **Compile error from a lingering reference:** a test or helper still names the deleted type — update/remove it.

## Rollback Strategy
Restore the deleted file and struct from version control.

## Notes for Future Tasks
- None; this closes out the generic-spawn-stream removal.

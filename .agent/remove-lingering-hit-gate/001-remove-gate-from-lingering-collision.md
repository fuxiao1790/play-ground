---
name: remove-gate-from-lingering-collision
description: LingeringAoeCollisionSystem collides every tick unconditionally instead of gating on AoeHitGateComponent.
---

# 001 - Remove the Gate From Lingering Collision

## Changes

### [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs)

1. `OnCreate` query builder ([line 37](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L37)):
   remove `.WithAllRW<AoeHitGateComponent>()`.
2. `OnUpdate` job construction ([line 86](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L86)):
   remove `HitGateHandle = SystemAPI.GetComponentTypeHandle<AoeHitGateComponent>(false),`.
3. `LingeringAoeCollisionJob` struct ([line 141](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L141)):
   remove `public ComponentTypeHandle<AoeHitGateComponent> HitGateHandle;` and the
   `DeltaTime` field if nothing else in the job needs it after this change
   (check — `DeltaTime` is currently only read for `hitGate.Remaining -=
   DeltaTime`; if that's its only use, remove the field and its assignment
   in `OnUpdate` too).
4. `Execute` ([lines 178, 197-208](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L178)):
   remove `NativeArray<AoeHitGateComponent> hitGates = chunk.GetNativeArray(ref HitGateHandle);`
   and the entire gate-check block:
   ```csharp
   AoeHitGateComponent hitGate = hitGates[i];
   hitGate.Remaining -= DeltaTime;
   if (hitGate.Remaining > 0f)
   {
       hitGates[i] = hitGate;
       continue;
   }

   hitGate.Remaining = hitGate.RepeatHitCooldownSeconds > 0f
       ? hitGate.Remaining + hitGate.RepeatHitCooldownSeconds
       : 0f;
   hitGates[i] = hitGate;
   ```
   The loop body becomes: for each enabled entity index, call
   `AoeCollisionCore.RunCollision(...)` directly, unconditionally, with the
   same arguments as today (unchanged).

Do not touch `AoeCollisionCore.RunCollision` itself, `ImpactAoeCollisionSystem.cs`,
or anything under `.agent/collision-lookup-result-cap/` — this task and that
plan touch disjoint code paths inside these files.

## Acceptance Criteria

- A lingering AOE with a target continuously inside its area registers a hit
  every tick (down to whatever `AoeHitGateComponent`'s per-target dedup —
  `seenTargetKeys` inside `RunCollision` — allows within a single tick; that
  dedup is unrelated to this gate and is untouched).
- No reference to `AoeHitGateComponent`, `HitGateHandle`, or `hitGates`
  remains in this file.
- `LingeringAoeCollisionJob` remains Burst-compiled.
- Compiles even though `AoeHitGateComponent` still exists as a type at this
  point in the task sequence (deleted in task 003) and is still present on
  the entity archetype (removed in task 002) — this task only stops the job
  from requiring/reading/writing it.

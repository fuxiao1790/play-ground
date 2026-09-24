---
name: targeted-and-combatroot
description: Drop TargetedSpawnCommand.OnHitSpawn and the matching CombatRoot.cs assignment/validation call sites
---

# 006 — Targeted Pipeline & CombatRoot

## Goal
Remove the last remaining `OnHitSpawnRef`-typed field (`TargetedSpawnCommand.OnHitSpawn`, never
consumed by any Targeted entity component — see index.md grounding) and update `CombatRoot.cs`,
which is the single place that builds all three command types and validates their child kinds.

## Dependencies
Tasks 003, 004, 005 complete (`OnHitSpawnRef` gone; `ProjectileHitPayload`/`AoeSpawnCommand`
already shrunk).

## Files to Modify
- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Step-by-Step

1. **`TargetedSpawnPipeline.cs`**
   - `TargetedSpawnCommand`: delete the `public OnHitSpawnRef OnHitSpawn;` field.

2. **`CombatRoot.cs`**
   - `AoeCommandFor(AoeSpawnRequest request, int aoeId)`: delete the
     `OnHitSpawn = request.OnHitSpawn,` line (matches task 005's `AoeSpawnRequest`/`AoeSpawnCommand`
     shrink).
   - `SpawnTemplateFor(in ProjectileSpawnCommand command)`: the `ProjectileHitPayload hp = template.HitPayload;`
     / `template.HitPayload = new ProjectileHitPayload(combat, hp.OnHitSpawn);` /
     `SpawnTemplateValidation.EnsureValidChildKind(hp.OnHitSpawn);` lines reference the deleted
     `OnHitSpawn` property (task 004 already dropped it from `ProjectileHitPayload`) — simplify to
     construct `new ProjectileHitPayload(combat)` and delete the `EnsureValidChildKind(hp.OnHitSpawn)`
     line. Keep the `StackEffect.Faction` reassignment logic unchanged.
   - `SpawnTemplateFor(in AoeSpawnCommand command)`: delete the
     `SpawnTemplateValidation.EnsureValidChildKind(template.OnHitSpawn);` line (the field no longer
     exists after task 005).
   - `SpawnTemplateFor(in TargetedSpawnCommand command)`: delete the
     `SpawnTemplateValidation.EnsureValidChildKind(template.OnHitSpawn);` line (the field no longer
     exists after step 1 above).

## Behavior to Preserve
- `TimedSpawnComponent` validation (`EnsureValidChildKind(timedSpawn)`) in both
  `SpawnTemplateFor(ProjectileSpawnCommand)` and `SpawnTemplateFor(AoeSpawnCommand)` — untouched.
- `StackEffect.Faction` zeroing for registry templates — untouched.

## Behavior to Change
- None observable (no live content sets an on-hit ref).

## Acceptance Criteria
- No reference to `OnHitSpawn`/`OnHitSpawnRef` remains in `TargetedSpawnPipeline.cs` or
  `CombatRoot.cs`.
- All three `SpawnTemplateFor` overloads and `AoeCommandFor` compile against the shrunk command/
  request/payload types from tasks 004–005.

## Validation
- `grep -n "OnHitSpawn" Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs Assets/Scripts/System/Core/CombatRoot.cs` returns nothing.
- At this point `grep -rn "OnHitSpawnRef\|OnHitTrigger" Assets/Scripts/` should return nothing at
  all across the whole `Assets/Scripts` tree — this is the completion signal for the source-code
  portion of the plan (tests and docs are separate tasks).
- Compile check deferred to the user.

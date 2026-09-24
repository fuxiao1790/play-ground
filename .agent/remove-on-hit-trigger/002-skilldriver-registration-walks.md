---
name: skilldriver-registration-walks
description: Remove every Impact*/OnHit* branch and the three BuildOnHitSpawnRef overloads from SkillDriver.cs
---

# 002 — SkillDriver Registration Walks

## Goal
`SkillDriver.cs` recursively walks compiled `Runtime*Definition` trees after compile to register
sounds, projectile/AOE/targeted types, and spawn templates. Remove every branch that walks into an
`Impact*Definition`/`OnHit*SpawnDefinition` field (now deleted in task 001), and delete the three
`BuildOnHitSpawnRef` overloads plus their call sites.

## Dependencies
Task 001 complete — `Runtime*Definition` on-hit fields no longer exist, so this task's edits are
required just to compile.

## Files to Modify
- `Assets/Scripts/Skills/SkillDriver.cs`

## Step-by-Step

Search `SkillDriver.cs` for every remaining reference to `Impact*Definition` / `OnHit*SpawnDefinition`
/ `OnHitSpawnRef` / `BuildOnHitSpawnRef` (task 001 will have made these non-compiling). Known call
sites from research (line numbers approximate, re-confirm against current file):

1. **`RegisterSoundsRecursive`** (~line 277–305): delete the three
   `RegisterSoundsRecursive(aoe.OnHit*Definition, depth + 1)` calls, the two
   `RegisterSoundsRecursive(targeted.OnHit*Definition, depth + 1)` calls, and the three
   `RegisterSoundsRecursive(projectile.Impact*Definition, depth + 1)` calls.

2. **`RegisterProjectileTypesRecursive`** (~line 582–632): delete the matching
   `if (x.OnHit*/Impact* != null) RegisterProjectileTypesRecursive(...)` blocks for aoeDef,
   targetedDef, and `p` (projectile).

3. **`RegisterSpawnTemplatesRecursive`** (~line 737–870): delete the `if (...OnHit*/Impact*... != null)
   RegisterSpawnTemplatesRecursive(...)` recursive-descent blocks for aoeDef, targetedDef, and
   projDef. This method also calls `BuildOnHitSpawnRef(aoeDef)` / `BuildOnHitSpawnRef(targetedDef)`
   / `BuildOnHitSpawnRef(projDef)` when constructing the registered template's spawn command —
   delete those call sites; the corresponding `OnHitSpawn = ...` field assignment on the command
   being built goes away too (the field itself is deleted in tasks 004/005/006, so leaving the
   assignment would not compile — coordinate: if this task lands before those, temporarily this
   assignment must also be deleted here since `BuildOnHitSpawnRef` itself is deleted in this task
   regardless of field lifetime).

4. **Three `BuildOnHitSpawnRef` overloads** (~line 971–1069): delete
   `BuildOnHitSpawnRef(RuntimeProjectileDefinition def)`,
   `BuildOnHitSpawnRef(RuntimeAoeDefinition def)`, and
   `BuildOnHitSpawnRef(RuntimeTargetedDefinition def)` entirely.

5. **`RegisterAoeTypesRecursive`** (~line 1170–1200): delete the `if (x.OnHit*/Impact* != null)
   RegisterAoeTypesRecursive(...)` blocks for aoeDef, targetedDef, projDef.

6. **`RegisterTargetedTypesRecursive`** (~line 1220–1245): delete the matching blocks for
   targetedDef, aoeDef, projDef.

7. Elsewhere in the file (~lines 1408, 1486, 1533, 1543, 1589 from research grep): these are
   inside command-construction helpers that take an `onHitSpawn = default` parameter and assign
   `OnHitSpawn = onHitSpawn` when building `ProjectileSpawnRequest`/`AoeSpawnRequest`/
   `ProjectileChildSpawnConfig`-style values. Once tasks 004/005/006 remove those parameters from
   the target constructors, remove the corresponding `onHitSpawn` argument here too. If those
   downstream constructors still exist when this task runs (before 004/005/006), leave these call
   sites passing `default` — do not invent a workaround; note the dependency and let 004/005/006
   finish the removal.

## Behavior to Preserve
- Sound registration, type registration, and spawn-template registration for
  `ChildSpawnSetup`/`AoeIntervalSpawnSetup`/`TargetedIntervalSpawnSetup`/`StackingDetonation`
  (interval and stack paths) — untouched.

## Behavior to Change
- None observable: every removed branch was walking a field that is now always absent (task 001),
  so these branches were already dead reads (`null` check would always fail once the field is
  gone — they simply no longer compile, which is why they must be deleted, not because they did
  anything at runtime).

## Acceptance Criteria
- `SkillDriver.cs` contains no reference to `OnHitTrigger`, `Impact*Definition`,
  `OnHit*SpawnDefinition`, `BuildOnHitSpawnRef`, or an `onHitSpawn`/`OnHitSpawn` argument that
  targets a now-deleted field.
- All five `Register*Recursive` methods still walk `ChildSpawnSetup`/interval/`StackingDetonation`
  correctly.

## Validation
- `grep -n "OnHitTrigger\|Impact.*Definition\|OnHit.*SpawnDefinition\|BuildOnHitSpawnRef" Assets/Scripts/Skills/SkillDriver.cs` returns nothing.
- Compile check deferred to the user.

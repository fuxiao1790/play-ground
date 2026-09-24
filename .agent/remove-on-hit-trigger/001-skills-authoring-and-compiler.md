---
name: skills-authoring-and-compiler
description: Delete OnHitTrigger.cs and every compile-time branch/field that exists to wire it
---

# 001 — Skills Authoring & Compile-Time

## Goal
Remove the `OnHitTrigger` authoring type, the compiler branch that wires it, the loadout-warning
branch that inspects its output, and the six `Runtime*Definition` fields it populates.

## Dependencies
None. First task.

## Files to Modify/Delete
- **Delete** `Assets/Scripts/Skills/Trigger/OnHitTrigger.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs`

## Step-by-Step

1. **Delete `OnHitTrigger.cs`.**

2. **`SkillSetCompiler.cs`**
   - In `CompileInternal`, delete the `else if (link is OnHitTrigger)` branch (currently between
     the `IntervalSpawnTrigger` branch and the `StackTrigger` branch, ~line 69–78). This removes
     the only call site of `AttachOnHitTarget`.
   - Delete the `AttachOnHitTarget` method entirely (~line 476–532).
   - In `GetManaCostMultiplier`: delete the three `GetManaCostMultiplier(projectile.Impact*Definition, includeCurrent: true)`
     terms from the `RuntimeProjectileDefinition` branch, and the three
     `GetManaCostMultiplier(aoe.OnHit*Definition, includeCurrent: true)` terms from the
     `RuntimeAoeDefinition` branch. Leave the `RuntimeTargetedDefinition` branch's
     `OnHitAoeSpawnDefinition`/`OnHitProjectileSpawnDefinition` terms removed too (those fields
     are deleted in this task — see below).
   - In `SumTriggeredSkillManaCosts`: same three-term removal in the `RuntimeProjectileDefinition`,
     `RuntimeAoeDefinition`, and `RuntimeTargetedDefinition` branches.
   - Keep `StackingDetonation` and `ChildSpawnSetup`/`AoeIntervalSpawnSetup`/
     `TargetedIntervalSpawnSetup` terms untouched — those belong to `StackTrigger` and
     `IntervalSpawnTrigger`, not `OnHitTrigger`.

3. **`SkillLoadoutCompiler.cs`**
   - Delete the four `AppendCompilerWarnings(...)` calls that pass `projectile.ImpactAoeDefinition`,
     `projectile.ImpactProjectileDefinition`, `aoe.OnHitAoeSpawnDefinition`,
     `aoe.OnHitProjectileSpawnDefinition` (~lines 87, 88, 97, 98). Confirm no other on-hit fields
     are checked in this file before finishing (`ImpactTargetedDefinition`,
     `OnHitTargetedSpawnDefinition` may also appear — remove those calls too if present).

4. **`RuntimeProjectileDefinition.cs`**
   - Delete the three properties `ImpactAoeDefinition`, `ImpactTargetedDefinition`,
     `ImpactProjectileDefinition` and their `// Compiled from OnHitTrigger...` comments
     (~lines 80–87). Keep `ChildSpawnSetup`, `AoeIntervalSpawnSetup`,
     `TargetedIntervalSpawnSetup`, `StackingDetonation`.

5. **`RuntimeAoeDefinition.cs`**
   - Delete the three properties `OnHitAoeSpawnDefinition`, `OnHitProjectileSpawnDefinition`,
     `OnHitTargetedSpawnDefinition` and their comments (~lines 52–61).

6. **`RuntimeTargetedDefinition.cs`**
   - Delete the two properties `OnHitAoeSpawnDefinition`, `OnHitProjectileSpawnDefinition` and the
     "Compiled from targeted-as-source trigger links in the next trigger task" comment
     (~lines 28–31). These were never wired by the compiler (see index.md grounding) — deleting
     them is behavior-neutral.

## Behavior to Preserve
- `IntervalSpawnTrigger` and `StackTrigger` compilation paths (unchanged).
- Mana-cost folding for `ChildSpawnSetup`/interval children and `StackingDetonation`.

## Behavior to Change
- `OnHitTrigger` no longer exists as a type; any loadout slot list containing one will fail to
  compile the *project*, not fail at runtime — there are none in the asset database (verified).

## Acceptance Criteria
- `OnHitTrigger.cs` no longer exists.
- No remaining reference to `OnHitTrigger`, `Impact*Definition`, or `OnHit*SpawnDefinition` in
  `SkillSetCompiler.cs`, `SkillLoadoutCompiler.cs`, or the three `Runtime*Definition.cs` files.
- `RuntimeProjectileDefinition`/`RuntimeAoeDefinition`/`RuntimeTargetedDefinition` still compile
  with `ChildSpawnSetup`/interval/`StackingDetonation` fields intact.

## Validation
- Search-based: `grep -rn "OnHitTrigger\|ImpactAoeDefinition\|ImpactProjectileDefinition\|ImpactTargetedDefinition\|OnHitAoeSpawnDefinition\|OnHitProjectileSpawnDefinition\|OnHitTargetedSpawnDefinition" Assets/Scripts/Skills/` should return nothing after this task (SkillDriver.cs is handled in task 002 — expect matches there until then).
- Compile check deferred to the user (Unity test runner not invoked by agents).

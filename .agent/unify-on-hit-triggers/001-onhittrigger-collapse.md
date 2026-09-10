# 001 — Introduce `OnHitTrigger`, collapse the compiler and validator

**Depends on:** nothing.
**Must land with:** [003-test-migration.md](003-test-migration.md) — the test
assemblies reference the deleted types directly.
**Scope:** Medium. One new file, four deletions, two files edited.

## Goal

One authoring type for every immediate spawn-on-hit link, carrying no attributes
of its own. The compiler picks the runtime field from the compiled source and
target types instead of from the trigger's class, and the triggered skill set
decides everything about what it spawns.

## Edits

### 1. New — `Assets/Scripts/Skills/Trigger/OnHitTrigger.cs`

```csharp
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Hit", fileName = "NewOnHitTrigger")]
    public sealed class OnHitTrigger : TriggerLink
    {
        public override SkillDefinitionTags SourceSkillTags =>
            SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;

        public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;
    }
}
```

No fields. `OnImpactProjectileTrigger`'s `spawnCount` and `spreadDegrees` are
**not** carried over — the effect set already owns burst size and spread through
`ProjectileDefinition.count` / `.spreadDegrees` and `MultipleProjectilesSupport`
(index I4). The trigger inherits the UI fields and the two mana-cost factors
from `TriggerLink`; those price the link, they do not describe the effect.

`SkillDefinitionTags.Any` is `Projectile | Aoe | Targeted`
(`SkillDefinitionTags.cs:14`).

Do **not** add `Targeted` to `SourceSkillTags` — targeted-as-source is unwired
(see index Scope).

### 2. Delete

- `Assets/Scripts/Skills/Trigger/OnImpactAoeTrigger.cs` (+ `.meta`)
- `Assets/Scripts/Skills/Trigger/OnImpactProjectileTrigger.cs` (+ `.meta`)
- `Assets/Scripts/Skills/Trigger/OnImpactTargetedTrigger.cs` (+ `.meta`)
- `Assets/Scripts/Skills/Trigger/OnAoeHitSpawnTrigger.cs` (+ `.meta`)

### 3. `Assets/Scripts/Skills/SkillSetCompiler.cs`

Replace the four `else if` branches at `:69-134` with one. `IntervalSpawnTrigger`
(`:66-68`) and `StackTrigger` (`:136-158`) are untouched and keep their relative
order.

```csharp
else if (link is OnHitTrigger)
{
    RuntimeSkillDefinition compiledTarget = CompileInternal(
        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
    if (AttachOnHitTarget(runtime, compiledTarget))
        ApplyIncomingTriggerManaCostMultiplier(compiledTarget, link);
}
```

New private helper, placed next to `ApplyIntervalSpawn`. It only wires; it never
edits the compiled child, which arrives fully resolved from `BuildRuntime`:

```csharp
// Selects the on-hit field from the compiled source and target types. Returns
// false when the pair has no runtime slot; the caller then skips the incoming
// mana-cost stamp, matching the per-branch behavior this replaced.
private static bool AttachOnHitTarget(
    RuntimeSkillDefinition source,
    RuntimeSkillDefinition target)
{
    if (target is RuntimeProjectileDefinition projectileTarget)
    {
        if (source is RuntimeProjectileDefinition projectileSource)
        {
            projectileSource.ImpactProjectileDefinition = projectileTarget;
            return true;
        }
        if (source is RuntimeAoeDefinition aoeSource)
        {
            aoeSource.OnHitProjectileSpawnDefinition = projectileTarget;
            return true;
        }
        return false;
    }

    if (target is RuntimeAoeDefinition aoeTarget)
    {
        if (source is RuntimeProjectileDefinition projectileSource)
        {
            projectileSource.ImpactAoeDefinition = aoeTarget;
            return true;
        }
        if (source is RuntimeAoeDefinition aoeSource)
        {
            aoeSource.OnHitAoeSpawnDefinition = aoeTarget;
            return true;
        }
        return false;
    }

    if (target is RuntimeTargetedDefinition targetedTarget)
    {
        if (source is RuntimeProjectileDefinition projectileSource)
        {
            projectileSource.ImpactTargetedDefinition = targetedTarget;
            return true;
        }
        if (source is RuntimeAoeDefinition aoeSource)
        {
            aoeSource.OnHitTargetedSpawnDefinition = targetedTarget;
            return true;
        }
        return false;
    }

    return false;
}
```

Load-bearing details:
- The helper **must not** write `Count` or `SpreadDegrees`. Deleting
  `SkillSetCompiler.cs:109-110` is the point of the attribute removal (index
  I4): the child keeps what `BuildRuntime` resolved from its own definition and
  supports, exactly as a directly cast projectile does.
- `ApplyIncomingTriggerManaCostMultiplier` runs **only on a successful attach**
  (index I2).
- A `null` `compiledTarget` falls through every `is` test and returns `false`.

### 4. `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

- Delete the `if (link is OnAoeHitSpawnTrigger) { … return; }` block at
  `:210-214`. Removing the early `return` is the point: `OnHitTrigger` links now
  reach the generic source/target tag checks at `:227-241`.
- Delete `ValidateAoeHitSpawnLink` (`:244-262`), `CanSourceAoeHitSpawn` (`:264-265`)
  and `CanTargetAoeHitSpawn` (`:267-268`).
- Check whether `AoeDefinitionBase` is still referenced in this file after the
  deletion; drop the `using` only if it became unused.

Nothing else in the validator changes. `SkillValidationWarningCode` is untouched.

## Explicitly not changed

- `RuntimeProjectileDefinition`, `RuntimeAoeDefinition`, `RuntimeTargetedDefinition`
  field sets (task 002 narrows one field type, nothing more).
- Any ECS system, spawn-template registration, or `SkillDriver` walk.
- `StackTrigger`, `IntervalSpawnTrigger`, `OnExpireTrigger`.
- `TriggerCatalog.cs` and every other polymorphic `TriggerLink` consumer (I8).

## Acceptance criteria

1. `OnHitTrigger.cs` exists, declares no fields of its own, and sets
   `Source = Projectile | Aoe`, `Target = Any`.
2. The four old trigger `.cs` files and their `.meta` files are gone; a
   project-wide search for `OnImpactAoeTrigger`, `OnImpactProjectileTrigger`,
   `OnImpactTargetedTrigger`, `OnAoeHitSpawnTrigger` returns hits only in
   `Docs/` (task 005) and `.agent/` plan files.
3. `SkillSetCompiler` has exactly one on-hit branch; `AttachOnHitTarget` covers
   all six source×target pairs and returns `false` for every other pair.
4. `AttachOnHitTarget` assigns fields only — a search for `.Count =` and
   `.SpreadDegrees =` inside it returns nothing. A Projectile→Projectile link
   yields a child whose `Count` and `SpreadDegrees` equal what the effect set
   compiles to on its own, identical to compiling that set as a root.
5. `ApplyIncomingTriggerManaCostMultiplier` is called once per on-hit link and
   only when a field was assigned.
6. `SkillLoadoutValidator` has no `OnAoeHitSpawnTrigger` reference and no
   `ValidateAoeHitSpawnLink` / `CanSourceAoeHitSpawn` / `CanTargetAoeHitSpawn`.
7. An AOE→AOE `OnHitTrigger` produces the same
   `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` wiring the two old triggers
   produced.
8. An AOE source with a projectile target now assigns
   `OnHitProjectileSpawnDefinition` (previously reachable only through
   `OnImpactProjectileTrigger`; `OnAoeHitSpawnTrigger` mis-assigned it to the
   AOE slot where it was silently dropped).
9. Unity compiles both the runtime and test assemblies with task 003 applied.

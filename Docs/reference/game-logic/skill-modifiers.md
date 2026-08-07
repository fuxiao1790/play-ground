# Skill Stat Modifiers

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

See [skill-system.md](./skill-system.md) for the broader skill authoring model
(Skills, Skill Sets, Trigger Links, Loadouts). This doc covers the skill
system's modifier surface: the support kind interfaces, every current source
that resolves through the shared numeric fold - Stat Modifier Supports,
player-level stat sources, and trigger-link-local resolves - and the current
augment support roster.

The fold itself is not a skill-system concept. See
[numeric-modifiers.md](../architecture/numeric-modifiers.md) for the general
pattern (`StatFold`, in `PlayGround.Common.Modifiers`) that any numeric stat
modifier in the codebase should follow; the skill system is its current
concrete consumer.

## Support Types

Each support derives from `SkillSupport`. Augment supports derive from
`StatModifierSupport`, which carries `SupportedSkillTags` for validation, and
then implement one or more kind interfaces. `ConversionSupport` stays separate:
it can replace the compiled runtime shape after normal stat and behavior baking.
`StackingSupport` is the current conversion support and marks its set
triggered-only.

The stat-specific augment interfaces are composable, not mutually exclusive
base classes:

```csharp
abstract class SkillSupport : ScriptableObject { }

abstract class StatModifierSupport : SkillSupport {
    public abstract SkillDefinitionTags SupportedSkillTags { get; }
}

interface IDamageModifiers {
    interface IBaseValueModifier {
        void CollectAdded(AddedSink sink);
    }
}

interface IAreaSizeModifiers {
    interface IIncreasedModifier {
        void CollectIncreases(IncreasedSink sink);
    }

    interface IMultiplierModifier {
        void CollectMultipliers(MultiplierSink sink);
    }
}

interface IProjectileSpeedModifiers {
    interface IMultiplierModifier {
        void CollectMultipliers(MultiplierSink sink);
    }
}

interface IProjectileLifetimeModifiers {
    interface IMultiplierModifier {
        void CollectMultipliers(MultiplierSink sink);
    }
}

interface IRateModifiers {
    interface IIncreasedModifier {
        void CollectIncreases(IncreasedSink sink);
    }
}

interface IPierceCountModifiers {
    interface IBaseValueModifier {
        void CollectAdded(AddedSink sink);
    }
}

interface IManaModifiers {
    interface IBaseValueModifier {
        void CollectAdded(AddedSink sink);
    }

    interface IIncreasedModifier {
        void CollectIncreases(IncreasedSink sink);
    }

    interface IMultiplierModifier {
        void CollectMultipliers(MultiplierSink sink);
    }
}

interface IProjectileBehaviorModifier {
    void ApplyToProjectile(ProjectileBehaviorContext ctx);
}

interface IAoeBehaviorModifier {
    void ApplyToAoe(AoeBehaviorContext ctx);
}

abstract class ConversionSupport : SkillSupport {
    public abstract bool ConvertsToTriggeredOnly { get; }
    public abstract RuntimeSkillDefinition Compile(
        SkillDefinition definition,
        RuntimeSkillDefinition runtime,
        SkillStatSnapshot snapshot);
}
```

A support may implement more than one kind. `PiercingSupport` is both an
`IPierceCountModifiers.IBaseValueModifier` for `PierceCount`, an
`IManaModifiers.IBaseValueModifier` for `ManaCost`, and an
`IProjectileBehaviorModifier` for `RepeatHitCooldown`. A support implementing
two identically shaped stat interfaces must give each an explicit interface
implementation, since one implicit method cannot serve both with different
bodies. Each interface receives only its constrained sink or behavior context,
so an increased modifier cannot write base values or multiply stats.

## Numeric Fold

Every numeric stat on a compiled skill (`Damage`, `AreaSize`, `Rate`,
`ManaCost`, `PierceCount`, `ProjectileSpeed`, `ProjectileLifetime`) resolves
through the shared fold in
[numeric-modifiers.md](../architecture/numeric-modifiers.md), combined across
every contributing support and player-level snapshot term via
`StatModifierAccumulator`. Support order never changes the numeric output.

Rate uses the same fold as other stats. `IncreasedRateSupport` and the player
`increasedRatePercent` are both authored as percent points (`25` means +25%)
and both feed the increased bucket, then cooldown is derived from the folded
rate:

```text
rate = Resolve(Rate, baseRate)
recoveryTime = 1 / max(0.01, rate)
```

## Player-Level Stat Resolution (Layer 1.5)

Types: `SkillStatSnapshot`, `SkillStatAggregator`

Stateless utility - not a chain tier. Pure aggregation function: inputs in,
flat snapshot out, no stored state. Collapses player-level Layer 1 stat
sources into a flat snapshot. The compiler feeds snapshot terms into the same
per-stat fold used by supports: multipliers where applicable, and increased
percentages where the stat is authored as increased scaling. Owns no data -
all sources come from Layer 1.

`SkillStatSnapshot` fields:
- `increasedRatePercent`
- `damageMultiplier`
- `areaSizeMultiplier`
- `baseEnergyGain`
- `increasedEnergyGain`
- `energyGainMultiplier`

Baking:
- `increasedRatePercent` contributes an increased percent to the `Rate` fold
- `damageMultiplier` contributes a multiplier to the `Damage` fold
- `areaSizeMultiplier` contributes a multiplier to the `AreaSize` fold
- `baseEnergyGain`, `increasedEnergyGain`, and `energyGainMultiplier`
  do not feed the per-skill `StatModifierAccumulator` fold used by Rate/
  Damage/AreaSize. They resolve through the shared `StatFold` into each interval
  trigger's own `energyPerSecond` at compile time; `increasedEnergyGain` is a
  direct multiplier (default `1`). An interval trigger is a per-edge concern
  with its own base value, not a set-level stat any support can attach to.
- `recoveryTime` is derived after folding: `recoveryTime = 1 / rate`

## Mana Cost Factor (Trigger Links)

Every `TriggerLink` has `manaCostIncreased` and `manaCostMultiplier`,
resolved per link via `TriggerLink.ResolveManaCostFactor()` and
`IntervalSpawnTrigger.ResolveEnergyPerSecond()` - the two single-source fold
resolves documented in
[numeric-modifiers.md#single-source-resolves](../architecture/numeric-modifiers.md#single-source-resolves).
`manaCostIncreased` is a direct multiplier (default `1`); `1.1` means `1.1x`
mana cost.
The resolved mana-cost factor is used for the interval child-energy
threshold, the initial active skill chain cost, and that link's triggered
skill cost. See [skill-system.md](./skill-system.md#projectiledefinition) for
how the factor aggregates across a full trigger chain.

## Current Augment Supports

| Support | Kind interface(s) | Stat / behavior contribution |
|---|---|---|
| Multiple Projectiles | `IManaModifiers.IBaseValueModifier`, `IManaModifiers.IIncreasedModifier`, `IManaModifiers.IMultiplierModifier`, `IProjectileBehaviorModifier` | Adds, increases, and multiplies `ManaCost` (three independent authored fields, same fold as player-level stat terms); adds projectile `count`, `spreadDegrees` |
| Multiple AOEs | `IManaModifiers.IBaseValueModifier`, `IManaModifiers.IIncreasedModifier`, `IManaModifiers.IMultiplierModifier`, `IAoeBehaviorModifier` | Adds, increases, and multiplies `ManaCost` (three independent authored fields, same fold as player-level stat terms); adds AOE `echoCount`, `scatterRadius` |
| Multiple Chains | `IManaModifiers.IBaseValueModifier`, `IManaModifiers.IIncreasedModifier`, `IManaModifiers.IMultiplierModifier`, `ITargetedBehaviorModifier` | Adds, increases, and multiplies `ManaCost`; adds targeted `echoCount` |
| Piercing | `IPierceCountModifiers.IBaseValueModifier`, `IManaModifiers.IBaseValueModifier`, `IProjectileBehaviorModifier` | Adds `ManaCost` and `PierceCount`; sets projectile `repeatHitCooldown` |
| Homing | `IManaModifiers.IBaseValueModifier`, `IProjectileBehaviorModifier` | Adds `ManaCost`; enables tracking and sets turn speed/query interval |
| Concentrated Effect | `IAreaSizeModifiers.IMultiplierModifier`, `IManaModifiers.IMultiplierModifier` | Multiplier on `AreaSize`; also multiplies `ManaCost` |
| Increased AOE Effect | `IAreaSizeModifiers.IIncreasedModifier`, `IManaModifiers.IIncreasedModifier` | Increased percent on `AreaSize`; also increases `ManaCost` |
| Faster Projectiles | `IProjectileSpeedModifiers.IMultiplierModifier`, `IProjectileLifetimeModifiers.IMultiplierModifier`, `IManaModifiers.IMultiplierModifier` | Multipliers on `ProjectileSpeed`, `ProjectileLifetime`; also multiplies `ManaCost` |
| Added Damage | `IDamageModifiers.IBaseValueModifier`, `IManaModifiers.IBaseValueModifier` | Adds `Damage`; may also add `ManaCost` (default `0`) |
| Increased Skill Speed | `IRateModifiers.IIncreasedModifier`, `IManaModifiers.IIncreasedModifier` | Increased percent on `Rate`; also increases `ManaCost` |

Supports also declare compatible skill tags:

| Support | Compatible tags |
|---|---|
| Multiple Projectiles | `Projectile` |
| Multiple AOEs | `Aoe` |
| Multiple Chains | `Targeted` |
| Piercing | `Projectile` |
| Homing | `Projectile` |
| Faster Projectiles | `Projectile` |
| Added Damage | `Projectile`, `Aoe` |
| Concentrated Effect | `Aoe`, `Targeted` |
| Increased AOE Effect | `Aoe`, `Targeted` |
| Increased Skill Speed | `Projectile`, `Aoe`, `Targeted` |

Example: putting Multiple Projectiles on an AOE skill is allowed, but it does
nothing and validation returns a warning.

`SkillDefinitionTags.Any` means `Projectile`, `Aoe`, or `Targeted`.

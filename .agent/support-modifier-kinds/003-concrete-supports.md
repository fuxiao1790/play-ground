# 003 — Re-implement concrete supports

Re-base each concrete support onto `StatModifierSupport` and implement the kind
interface(s) it needs. Depends on 002.

**Keep every file path, class name, namespace, and serialized field name** so
`.asset` GUIDs and field serialization stay valid (no migration). Keep `[Min]` /
`[FormerlySerializedAs]` attributes as-is. Every class below is
`sealed class X : StatModifierSupport, <interfaces>`.

## Kind 1 — `IBaseValueModifier`
### AddedDamageSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.Damage, addedDamage);
```

## Kind 2 — `IIncreasedModifier`
### IncreasedAoeSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;
public void CollectIncreases(IncreasedSink sink)
    => sink.Add(SkillStat.AreaSize, areaSizeMultiplier - 1f);
```
### IncreasedRecoverySpeedSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;
public void CollectIncreases(IncreasedSink sink)
    => sink.Add(SkillStat.RecoverySpeed, recoverySpeedMultiplier - 1f);
```
(Drops the now-removed `RecoverySpeedMultiplier` override and the empty `Apply`.)

## Kind 3 — `IMultiplierModifier`
### ConcentratedEffectSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;
public void CollectMultipliers(MultiplierSink sink)
    => sink.Add(SkillStat.AreaSize, areaSizeMultiplier); // Post (default)
```
### FasterProjectilesSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;
public void CollectMultipliers(MultiplierSink sink)
{
    sink.Add(SkillStat.ProjectileSpeed, speedMultiplier);
    sink.Add(SkillStat.ProjectileLifetime, lifetimeMultiplier);
}
```

## Kind 4 — `IProjectileBehaviorModifier`
### MultipleProjectilesSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;
public void ApplyToProjectile(ProjectileBehaviorContext ctx)
{
    ctx.Count = count;
    ctx.SpreadDegrees = spreadDegrees;
}
```
### HomingSupport
```csharp
public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;
public void ApplyToProjectile(ProjectileBehaviorContext ctx)
    => ctx.EnableTracking(trackingTurnSpeedDegrees, trackingQueryIntervalSeconds);
```

## Multi-kind — `IBaseValueModifier` + `IProjectileBehaviorModifier`
### PiercingSupport (the composition case)
```csharp
public sealed class PiercingSupport : StatModifierSupport,
    IBaseValueModifier, IProjectileBehaviorModifier
{
    [SerializeField, Min(0)] private int pierceCount = 2;
    [SerializeField, Min(0f)] private float repeatHitCooldown = 0.5f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

    // additive: stacks with base authored pierce + other pierce sources
    public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.PierceCount, pierceCount);

    // behavior: repeat-hit gate is a config knob, set (last wins on conflict)
    public void ApplyToProjectile(ProjectileBehaviorContext ctx)
        => ctx.RepeatHitCooldown = repeatHitCooldown;
}
```
Field names unchanged → asset loads as-is. Behavior change: two Piercing supports now
sum pierce (was last-wins). A single Piercing support yields the same number as before.

The `if (def is ProjectileDefinition p)` guards disappear from all behavior supports —
the compiler only calls `ApplyToProjectile` for projectile definitions.

## Acceptance criteria
- All eight supports compile against `StatModifierSupport` + their interfaces.
- Numeric supports go through sinks; behavior supports through contexts; Piercing does
  both, each through its own narrow surface.
- Serialized field names unchanged → assets load without reserialization.

## Scope: medium. Eight small files; Piercing is the only multi-interface one.

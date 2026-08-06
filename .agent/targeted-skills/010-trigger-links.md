# 010 — Trigger links

**Depends on:** 009. **Scope:** medium. **Risk:** the interval-trigger traps.

## Why

Two new links let a targeted skill be reached from a projectile or AOE. `StackTrigger` needs no new
type — it works once `Any` widens in task 011.

## New files

`Assets/Scripts/Skills/Trigger/OnImpactTargetedTrigger.cs`

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Impact Targeted")]
public sealed class OnImpactTargetedTrigger : TriggerLink
{
    public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
    public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Targeted;
}
```

- **No fields**, mirroring `OnImpactAoeTrigger`. `OnImpactProjectileTrigger` carries
  `spawnCount`/`spreadDegrees` because a projectile burst needs an angular fan; a chain has no aim
  direction to fan around. Chain count is whatever the effect set authors.
- Anchor **and** origin are both the impact point — they only diverge for root casts.

`Assets/Scripts/Skills/Trigger/TargetedIntervalSpawnTrigger.cs`

```csharp
public sealed class TargetedIntervalSpawnTrigger : IntervalSpawnTrigger
{
    [Min(0)] public int count;   // additive with the child's own count
}
```

- Inherits `energyPerSecond`, `ResolveEnergyPerSecond`, and `ManaToEnergyCost` unchanged.
- `count` is **additive** with the child definition's own count, floored to 1 — matching
  `AoeIntervalSpawnTrigger.echoCount` and `OnImpactProjectileTrigger.spawnCount`.
- **No geometry field.** Its two siblings pair their count with `scatterRadius` /
  `sideSpreadDegrees` because both place a shape or a direction; a chain places neither.
- Source tags `Projectile | Aoe`, target tag `Targeted`. A pulse-AOE source validates with a
  warning and compiles no timed-child setup, exactly as the existing interval triggers do.

## Changes

`Assets/Scripts/Skills/SkillSetCompiler.cs`

- `OnImpactTargetedTrigger`: compile the effect to a `RuntimeTargetedDefinition` and bake it onto
  `RuntimeProjectileDefinition.ImpactTargetedDefinition` (projectile source) or
  `RuntimeAoeDefinition.OnHitTargetedSpawnDefinition` (AOE source). Both are new fields on those
  runtime definitions, materialised as an `OnHitSpawnRef` carrying the targeted template key and
  kind — the same slim reference shape the existing on-hit spawns use.
- `TargetedIntervalSpawnTrigger`: compile a `RuntimeTargetedIntervalSpawnSetup` onto
  `RuntimeProjectileDefinition` / `RuntimeAoeDefinition`, carrying `JitterSeed`, `TemplateKey`,
  `EnergyPerSecond`, `EnergyThreshold`, and the summed `Count`.
- Threshold is `max(0.001, childManaCost * ResolveManaCostFactor())`, unchanged from the other two
  interval triggers.

`ProjectileDiscreteCollisionSystem` / `ProjectileContinuousCollisionSystem` / `AoeCollisionCore`

- Emit the targeted spawn event when the compiled on-hit ref's kind is `Targeted` or
  `LingeringTargeted`. Set both `Position` and `AcquireAnchor` to the impact point.
- These are the dispatch sites task 002 made throw on unhandled kinds; this task supplies the bodies.

## Acceptance criteria

- EditMode: projectile set → `OnImpactTargetedTrigger` → targeted set compiles, and the projectile's
  on-hit ref carries the targeted template key with the right kind.
- EditMode: AOE set → `OnImpactTargetedTrigger` → targeted set compiles onto the AOE's on-hit slot.
- EditMode: lingering AOE → `TargetedIntervalSpawnTrigger` → targeted set compiles a timed-child
  setup with a positive threshold.
- EditMode: pulse AOE source produces a validation warning and **no** timed-child setup.
- EditMode: trigger `count = 2` on a child whose own `count` is 3 compiles to 5.
- EditMode: `StackTrigger` from a targeted applicator to a stacking set compiles and bakes the
  stack snapshot into the targeted hit payload.
- EditMode: an energy threshold larger than what the source can accrue over its lifetime produces
  the validation warning from task 013 (the child would otherwise never spawn — a trap this project
  has hit before).
- All existing trigger-compile tests pass unchanged.

## Notes

Targeted as a trigger **source** (`OnTargetedHitTrigger` → spawn AOE/projectile at each hit) is
**v2**. Task 009 declares the runtime slots so nothing blocks it; do not compile them here.

Per-instance `JitterSeed` restamping matters for interval-spawned children — the compiled template
shares one seed across every cast, and task 003's expansion restamps per fork. Do not remove that.

# 004 — Compiler fold + behavior application + validator

Rewire `SkillSetCompiler` to build one accumulator (supports + snapshot), apply
behavior through contexts, and resolve every numeric stat + recovery through the
fold. Swap the validator's base-type check. Depends on 001–003.

## `SkillSetCompiler.cs`

### `CompileDefinition` (rewrite)
Dispatch is by **interface**, so a multi-kind support (Piercing) is visited by more
than one branch.
```
defCopy = definition.DeepCopy()

acc = new StatModifierAccumulator()
SnapshotModifiers.Contribute(acc, snapshot)     // player tier
foreach support in supports:
    if support is IBaseValueModifier  b: b.CollectAdded(new AddedSink(acc))
    if support is IIncreasedModifier  i: i.CollectIncreases(new IncreasedSink(acc))
    if support is IMultiplierModifier m: m.CollectMultipliers(new MultiplierSink(acc))

// behavior on the copy (disjoint fields from the numeric fold)
foreach support in supports:
    if defCopy is ProjectileDefinition p && support is IProjectileBehaviorModifier pb:
        pb.ApplyToProjectile(new ProjectileBehaviorContext(p))
    else if defCopy is AoeDefinitionBase a && support is IAoeBehaviorModifier ab:
        ab.ApplyToAoe(new AoeBehaviorContext(a))

runtime = BuildRuntime(defCopy, acc)
runtime = ApplyConversionSupports(definition, runtime, supports, snapshot)
return runtime
```
Note each branch uses `if`, not `else if`, across the three numeric kinds so Piercing
(`IBaseValueModifier` + `IProjectileBehaviorModifier`) is collected as base value
*and* applied as behavior. Behavior supports run on the copy; `BuildRuntime` then
reads behavior fields from the (behavior-mutated) copy and folds numeric fields from
the copy's **untouched** base values — the two field sets are disjoint, so order is
irrelevant.

The old `ApplySupports(def, baseDefinition, supports)` and the two-arg
`Apply(def, baseDefinition)` call site are deleted.

### `SnapshotModifiers.Contribute(acc, snapshot)` (new helper)
```
acc.AddMultiplier(SkillStat.Damage,   snapshot.DamageMultiplier,   Post);
acc.AddMultiplier(SkillStat.AreaSize, snapshot.AreaSizeMultiplier, Post);
// CastSpeedMultiplier is a recovery-time multiplier, applied in ResolveRecoveryTime.
```

### `BuildRuntime(defCopy, acc)` (modify)
Replace the inline `* snapshot.X` scaling with fold reads:
- Projectile: `Damage = max(0, acc.Resolve(Damage, p.damage))`,
  `Speed = acc.Resolve(ProjectileSpeed, p.speed)`,
  `Lifetime = acc.Resolve(ProjectileLifetime, p.lifetime)`,
  `PierceCount = max(0, RoundToInt(acc.Resolve(PierceCount, p.pierceCount)))`.
  Count/Spread/Jitter/RepeatHitCooldown/Tracking/DirectDamage copied from the
  (behavior-mutated) `defCopy` as today. Pierce now comes from the fold, not the copy.
- AOE: `AreaSize = max(0.01, acc.Resolve(AreaSize, a.baseAreaSize))`,
  `Damage = max(0, acc.Resolve(Damage, a.damage))`. Other fields as today.
- Crit fields still copied straight from `snapshot` (not modifiers).

`BuildRuntime` takes the accumulator instead of the bare snapshot.

### `ResolveRecoveryTime(baseRecoveryTime, acc, snapshot)` (modify)
```
speedFactor = acc.Resolve(RecoverySpeed, 1f);                 // base speed = 1
time = baseRecoveryTime * snapshot.CastSpeedMultiplier / max(0.01, speedFactor);
return max(0.01, time);
```
Reads increased-recovery-speed from the fold instead of summing
`RecoverySpeedMultiplier`. Signature drops the `supports` walk; takes `acc`.
Build the accumulator once in `Compile` and pass it to both `BuildRuntime` and
`ResolveRecoveryTime` (recovery still resolved at the
[SkillSetCompiler.cs:27](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L27) call
site). Note: the accumulator is per skill set (per `CompileDefinition`); recovery
must use the same set's accumulator.

> Implementation note: `Compile` currently resolves recovery after
> `CompileDefinition` returns. Refactor so `CompileDefinition` returns (or exposes)
> the accumulator it built, or resolve recovery inside `CompileDefinition` using
> `set.Skill.BaseRecoveryTime`. Pick whichever keeps a single accumulator per set.

## `SkillLoadoutValidator.cs` (modify)
[Line 73](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L73):
`if (supports[i] is not AdditiveSupport support)` →
`if (supports[i] is not StatModifierSupport support)`.
Same skip behavior for `ConversionSupport` and bare `SkillSupport`; now covers all
four numeric/behavior kinds for tag validation.

## Acceptance criteria
- `SkillSetCompiler` no longer references `Apply`, `RecoverySpeedMultiplier`, or
  per-stat `snapshot.X` multiplies outside `SnapshotModifiers`.
- Numeric output is order-independent across supports on the same stat.
- All existing EditMode tests pass (parity); see 005.
- Validator still emits `UnsupportedSupportForSkill` for a projectile behavior
  support on an AOE skill
  ([SkillValidationEditModeTests.cs:27-39](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L27-L39)).

## Scope: medium. The compiler is the heart of the change.

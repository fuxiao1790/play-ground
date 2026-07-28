---
name: mana-cost-modifier-supports
description: Replace the flat generic kind interfaces with one per-stat interface family (mirroring IManaModifiers) for every SkillStat a support currently modifies
---

# 001 — Per-Stat Modifier Interface Families

## Depends On

None. Independent of 002 (touches different files).

## Scope

Fifth revision, and a scope expansion. Round 4 introduced `IManaModifiers`
as a parallel interface family solely for `ManaCost`, living alongside the
existing flat, generic `IBaseValueModifier`/`IIncreasedModifier`/
`IMultiplierModifier` (each reused across every stat, disambiguated only by
which `SkillStat` enum value a support passes to `sink.Add(...)`). User
feedback: "every kind of modifier should follow this not just mana." This
draft generalizes the `IManaModifiers` pattern to **every stat a support
currently modifies**, not only `ManaCost`: one dedicated interface family per
`SkillStat`, each shaped exactly like `IManaModifiers`. The old flat generic
interfaces are retired — every support migrates to stat-specific interfaces,
using explicit interface implementation wherever a support touches more than
one stat through the same kind (e.g. `AddedDamageSupport` touching both
`Damage` and `ManaCost` via `CollectAdded`).

This is a genuinely larger change than prior rounds: it touches every
support's **primary** stat contribution, not just the mana-cost add-on, and
retires code that's documented at length in `skill-system.md`. Flagging that
plainly rather than downplaying it — this is a real interface-architecture
change, not a mana-cost feature anymore on the support side.

## Files

- `Assets/Scripts/Skills/Support/ModifierKindInterfaces.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Support/AddedDamageSupport.cs`
- `Assets/Scripts/Skills/Support/MultipleProjectilesSupport.cs`
- `Assets/Scripts/Skills/Support/PiercingSupport.cs`
- `Assets/Scripts/Skills/Support/HomingSupport.cs`
- `Assets/Scripts/Skills/Support/MultipleAoesSupport.cs`
- `Assets/Scripts/Skills/Support/IncreasedAoeSupport.cs`
- `Assets/Scripts/Skills/Support/IncreasedRateSupport.cs`
- `Assets/Scripts/Skills/Support/ConcentratedEffectSupport.cs`
- `Assets/Scripts/Skills/Support/FasterProjectilesSupport.cs`
- `Assets/Tests/EditMode/ModifierFoldEditModeTests.cs` — **must** change, not
  optional: it declares 3 test-only generic support classes
  (`TestAddedSupport`/`TestIncreasedSupport`/`TestMultiplierSupport`,
  [ModifierFoldEditModeTests.cs:316-351](../../Assets/Tests/EditMode/ModifierFoldEditModeTests.cs#L316-L351))
  implementing the bare `IBaseValueModifier`/`IIncreasedModifier`/
  `IMultiplierModifier` this task deletes. These aren't just renamed —
  user feedback: "old test that are testing obsolete logic should be
  removed." These 3 classes exist solely to test the *old* mechanism (one
  generic interface, any stat picked at runtime via an enum argument) — that
  mechanism is exactly what this task retires, so patching them to a fake
  fixed stat would be keeping obsolete-shaped scaffolding on artificial life
  support. See the dedicated section below: they're deleted, and the 2 tests
  they powered are rewritten to test what they actually verify more
  directly.

`StatModifierSupport.cs` stays untouched — same as every prior round.

## Per-Stat Interface Coverage

Only the `(SkillStat, kind)` pairs an actual **production** support
implements get a nested interface — no speculative unused combinations, and
no interface members kept alive purely to satisfy a test (see below:
`ModifierFoldEditModeTests.cs`'s generic helpers are deleted, not migrated,
specifically so they don't force `IDamageModifiers` to carry two kinds
nothing in production ever uses). `IManaModifiers` is the one family with all
3 kinds, because the mana-cost feature itself needs all 3 in production.

| `SkillStat` | Interface family | Kinds declared | Current implementer(s) |
|---|---|---|---|
| `Damage` | `IDamageModifiers` | `IBaseValueModifier` | `AddedDamageSupport` |
| `AreaSize` | `IAreaSizeModifiers` | `IIncreasedModifier`, `IMultiplierModifier` | `IncreasedAoeSupport`, `ConcentratedEffectSupport` |
| `ProjectileSpeed` | `IProjectileSpeedModifiers` | `IMultiplierModifier` | `FasterProjectilesSupport` |
| `ProjectileLifetime` | `IProjectileLifetimeModifiers` | `IMultiplierModifier` | `FasterProjectilesSupport` |
| `Rate` | `IRateModifiers` | `IIncreasedModifier` | `IncreasedRateSupport` |
| `PierceCount` | `IPierceCountModifiers` | `IBaseValueModifier` | `PiercingSupport` |
| `ManaCost` | `IManaModifiers` | `IBaseValueModifier`, `IIncreasedModifier`, `IMultiplierModifier` | all 9 supports may implement any subset |

## Implementation

### `ModifierKindInterfaces.cs` — full replacement

```csharp
using PlayGround.Skills.Modifiers;

namespace PlayGround.Skills
{
    public interface IDamageModifiers
    {
        public interface IBaseValueModifier
        {
            void CollectAdded(AddedSink sink);
        }
    }

    public interface IAreaSizeModifiers
    {
        public interface IIncreasedModifier
        {
            void CollectIncreases(IncreasedSink sink);
        }

        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IProjectileSpeedModifiers
    {
        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IProjectileLifetimeModifiers
    {
        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IRateModifiers
    {
        public interface IIncreasedModifier
        {
            void CollectIncreases(IncreasedSink sink);
        }
    }

    public interface IPierceCountModifiers
    {
        public interface IBaseValueModifier
        {
            void CollectAdded(AddedSink sink);
        }
    }

    public interface IManaModifiers
    {
        public interface IBaseValueModifier
        {
            void CollectAdded(AddedSink sink);
        }

        public interface IIncreasedModifier
        {
            void CollectIncreases(IncreasedSink sink);
        }

        public interface IMultiplierModifier
        {
            void CollectMultipliers(MultiplierSink sink);
        }
    }

    public interface IProjectileBehaviorModifier
    {
        void ApplyToProjectile(ProjectileBehaviorContext ctx);
    }

    public interface IAoeBehaviorModifier
    {
        void ApplyToAoe(AoeBehaviorContext ctx);
    }
}
```

The old flat `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier`
(previously reused across every stat) are **deleted**, not kept alongside the
new ones — once every support migrates, nothing implements them, and this
codebase doesn't keep unused types around.

### `SkillSetCompiler.CollectSupportModifiers` — full replacement

([SkillSetCompiler.cs:180-202](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L180-L202)),
one `is` check per row in the coverage table above:

```csharp
private static void CollectSupportModifiers(
    StatModifierAccumulator modifiers,
    IReadOnlyList<SkillSupport> supports)
{
    if (modifiers == null || supports == null)
        return;

    for (int i = 0; i < supports.Count; i++)
    {
        SkillSupport support = supports[i];
        if (support == null)
            continue;

        if (support is IDamageModifiers.IBaseValueModifier damageAdded)
            damageAdded.CollectAdded(new AddedSink(modifiers));

        if (support is IPierceCountModifiers.IBaseValueModifier pierceCountAdded)
            pierceCountAdded.CollectAdded(new AddedSink(modifiers));

        if (support is IAreaSizeModifiers.IIncreasedModifier areaSizeIncreased)
            areaSizeIncreased.CollectIncreases(new IncreasedSink(modifiers));

        if (support is IAreaSizeModifiers.IMultiplierModifier areaSizeMultiplier)
            areaSizeMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

        if (support is IProjectileSpeedModifiers.IMultiplierModifier speedMultiplier)
            speedMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

        if (support is IProjectileLifetimeModifiers.IMultiplierModifier lifetimeMultiplier)
            lifetimeMultiplier.CollectMultipliers(new MultiplierSink(modifiers));

        if (support is IRateModifiers.IIncreasedModifier rateIncreased)
            rateIncreased.CollectIncreases(new IncreasedSink(modifiers));

        if (support is IManaModifiers.IBaseValueModifier manaAdded)
            manaAdded.CollectAdded(new AddedSink(modifiers));

        if (support is IManaModifiers.IIncreasedModifier manaIncreased)
            manaIncreased.CollectIncreases(new IncreasedSink(modifiers));

        if (support is IManaModifiers.IMultiplierModifier manaMultiplier)
            manaMultiplier.CollectMultipliers(new MultiplierSink(modifiers));
    }
}
```

10 checks, flat and explicit — matches this method's existing style (it was
already a flat sequence of `if` checks before this change; growing from 3 to
10 keeps the same shape rather than introducing a lookup table or reflection,
consistent with [coding-standards.md:93-95](../../Docs/coding-standards.md#L93-L95)
("Reflection is only for editor tooling... not for gameplay decisions").

### Per-support migration

Every support that implements more than one interface sharing an identical
method signature (e.g. both `IDamageModifiers.IBaseValueModifier` and
`IManaModifiers.IBaseValueModifier`, both declaring `CollectAdded(AddedSink)`)
**must** use explicit interface implementation for each — this was already
true for the mana-only interfaces in round 4 and now also applies to a
support's own-stat interface once it stops being the flat generic one.

**`AddedDamageSupport.cs`** (Damage + Mana, both `IBaseValueModifier`-shaped):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Added Damage", fileName = "AddedDamageSupport")]
public sealed class AddedDamageSupport : StatModifierSupport, IDamageModifiers.IBaseValueModifier, IManaModifiers.IBaseValueModifier
{
    [SerializeField] private float addedDamage = 5f;
    [SerializeField, Min(0f)] private float manaCostAdded;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

    void IDamageModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.Damage, addedDamage);
    void IManaModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);
}
```

**`MultipleProjectilesSupport.cs`** (Mana only — no own-stat "added"
contribution, so no explicit qualification needed):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple Projectiles", fileName = "MultipleProjectilesSupport")]
public sealed class MultipleProjectilesSupport : StatModifierSupport, IManaModifiers.IBaseValueModifier, IProjectileBehaviorModifier
{
    [SerializeField, Min(1)] private int count = 3;
    [SerializeField, Min(0f)] private float spreadDegrees = 30f;
    [SerializeField, Min(0f)] private float manaCostAdded = 4f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

    public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);

    public void ApplyToProjectile(ProjectileBehaviorContext ctx)
    {
        ctx.Count += count;
        ctx.SpreadDegrees += spreadDegrees;
    }
}
```

**`PiercingSupport.cs`** (PierceCount + Mana, both `IBaseValueModifier`-shaped):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Piercing", fileName = "PiercingSupport")]
public sealed class PiercingSupport : StatModifierSupport, IPierceCountModifiers.IBaseValueModifier, IManaModifiers.IBaseValueModifier, IProjectileBehaviorModifier
{
    [SerializeField, Min(0)] private int pierceCount = 2;
    [SerializeField, Min(0f)] private float repeatHitCooldown = 0.5f;
    [SerializeField, Min(0f)] private float manaCostAdded = 3f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

    void IPierceCountModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.PierceCount, pierceCount);
    void IManaModifiers.IBaseValueModifier.CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);

    public void ApplyToProjectile(ProjectileBehaviorContext ctx) => ctx.RepeatHitCooldown = repeatHitCooldown;
}
```

**`HomingSupport.cs`** (Mana only, same shape as `MultipleProjectilesSupport`):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Homing", fileName = "HomingSupport")]
public sealed class HomingSupport : StatModifierSupport, IManaModifiers.IBaseValueModifier, IProjectileBehaviorModifier
{
    [SerializeField] private float trackingTurnSpeedDegrees = 180f;
    [SerializeField] private float trackingQueryIntervalSeconds = 0.1f;
    [SerializeField, Min(0f)] private float manaCostAdded = 3f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

    public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);

    public void ApplyToProjectile(ProjectileBehaviorContext ctx) => ctx.EnableTracking(trackingTurnSpeedDegrees, trackingQueryIntervalSeconds);
}
```

**`MultipleAoesSupport.cs`** (Mana only, same shape):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Multiple AOEs", fileName = "MultipleAoesSupport")]
public sealed class MultipleAoesSupport : StatModifierSupport, IManaModifiers.IBaseValueModifier, IAoeBehaviorModifier
{
    [SerializeField, Min(1)] private int echoCount = 3;
    [SerializeField, Min(0f)] private float scatterRadius = 2f;
    [SerializeField, Min(0f)] private float manaCostAdded = 4f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

    public void CollectAdded(AddedSink sink) => sink.Add(SkillStat.ManaCost, manaCostAdded);

    public void ApplyToAoe(AoeBehaviorContext ctx)
    {
        ctx.EchoCount += echoCount;
        ctx.ScatterRadius += scatterRadius;
    }
}
```

**`IncreasedAoeSupport.cs`** (AreaSize + Mana, both `IIncreasedModifier`-shaped;
gains new `manaCostIncreasedPercent`):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Aoe Effect", fileName = "IncreasedAoeSupport")]
public sealed class IncreasedAoeSupport : StatModifierSupport, IAreaSizeModifiers.IIncreasedModifier, IManaModifiers.IIncreasedModifier
{
    [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
    private float areaSizeMultiplier = 1.5f;
    [SerializeField] private float manaCostIncreasedPercent;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

    void IAreaSizeModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.AreaSize, areaSizeMultiplier - 1f);
    void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreasedPercent);
}
```

**`IncreasedRateSupport.cs`** (Rate + Mana, both `IIncreasedModifier`-shaped;
gains new `manaCostIncreasedPercent`):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Increased Skill Speed", fileName = "IncreasedRateSupport")]
public sealed class IncreasedRateSupport : StatModifierSupport, IRateModifiers.IIncreasedModifier, IManaModifiers.IIncreasedModifier
{
    [SerializeField] private float increasedRatePercent = 0.5f; // 0.5 = +50% rate
    [SerializeField] private float manaCostIncreasedPercent;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Any;

    void IRateModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.Rate, increasedRatePercent);
    void IManaModifiers.IIncreasedModifier.CollectIncreases(IncreasedSink sink) => sink.Add(SkillStat.ManaCost, manaCostIncreasedPercent);
}
```

**`ConcentratedEffectSupport.cs`** (AreaSize + Mana, both
`IMultiplierModifier`-shaped; gains new `manaCostMultiplier`):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Concentrated Effect", fileName = "ConcentratedEffectSupport")]
public sealed class ConcentratedEffectSupport : StatModifierSupport, IAreaSizeModifiers.IMultiplierModifier, IManaModifiers.IMultiplierModifier
{
    [SerializeField, FormerlySerializedAs("sizeMultiplier"), Min(0.01f)]
    private float areaSizeMultiplier = 0.75f;
    [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Aoe;

    void IAreaSizeModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.AreaSize, areaSizeMultiplier);
    void IManaModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
}
```

**`FasterProjectilesSupport.cs`** (ProjectileSpeed + ProjectileLifetime +
Mana, all 3 `IMultiplierModifier`-shaped — the one support needing 3
explicit implementations; gains new `manaCostMultiplier`):

```csharp
[CreateAssetMenu(menuName = "PlayGround/Skills/Supports/Faster Projectiles", fileName = "FasterProjectilesSupport")]
public sealed class FasterProjectilesSupport : StatModifierSupport, IProjectileSpeedModifiers.IMultiplierModifier, IProjectileLifetimeModifiers.IMultiplierModifier, IManaModifiers.IMultiplierModifier
{
    [SerializeField, Min(0.01f)] private float speedMultiplier = 1.5f;
    [SerializeField, Min(0.01f)] private float lifetimeMultiplier = 1.2f;
    [SerializeField, Min(0f)] private float manaCostMultiplier = 1f;

    public override SkillDefinitionTags SupportedSkillTags => SkillDefinitionTags.Projectile;

    void IProjectileSpeedModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ProjectileSpeed, speedMultiplier);
    void IProjectileLifetimeModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ProjectileLifetime, lifetimeMultiplier);
    void IManaModifiers.IMultiplierModifier.CollectMultipliers(MultiplierSink sink) => sink.Add(SkillStat.ManaCost, manaCostMultiplier);
}
```

This support previously set `ProjectileSpeed` and `ProjectileLifetime` from
one implicit method body; splitting into per-stat interfaces means two
explicit one-line methods instead of one two-line method. No behavior change
— still the same support, same fields, same two stats scaled together by
whoever equips it — just dispatched through two named interface members
instead of one.

### `ModifierFoldEditModeTests.cs` — delete obsolete helpers, rewrite what they tested

The 3 test-only generic helper classes
([:316-351](../../Assets/Tests/EditMode/ModifierFoldEditModeTests.cs#L316-L351))
and their 3 factory methods
([:256-283](../../Assets/Tests/EditMode/ModifierFoldEditModeTests.cs#L256-L283))
exist to implement the bare generic interfaces this task deletes, targeting
an arbitrary `SkillStat` picked at runtime. That capability — one generic
interface, any stat chosen by an enum argument — is exactly the old
mechanism this task retires. **Delete all 6**, not migrate: per user
feedback, "old test that are testing obsolete logic should be removed."
Patching them to a hardcoded `IDamageModifiers` (an earlier version of this
plan's draft) would have forced `IDamageModifiers` to carry
`IIncreasedModifier`/`IMultiplierModifier` members with **zero production
implementer**, existing solely to keep a test compiling — that's the same
"metadata added just for tests" smell
[coding-standards.md's Test Hooks section](../../Docs/coding-standards.md#L381-L393)
warns against, just on an interface instead of a field.

The two tests that used them exercised two genuinely different things, so
each gets rewritten to test that thing directly instead:

**`AddedIncreasedAndMultiplierComposePerFormula`** tested "does added +
increased + multiplier compose per the documented formula." Task 003's new
`CompilerCombinesManaCostFoldOrderAcrossSupports` already covers this through
real production `ManaCost` supports (the one stat with real supports for all
3 kinds after this plan ships) — but that's an *integration* test (compiler
wiring). Keep a *unit* test of the formula itself, calling
`StatModifierAccumulator` directly with no support/compiler/ScriptableObject
involved at all:

```csharp
[Test]
public void AddedIncreasedAndMultiplierComposePerFormula()
{
    var accumulator = new StatModifierAccumulator();
    accumulator.AddAdded(SkillStat.Damage, 5f);
    accumulator.AddIncreased(SkillStat.Damage, 1f);
    accumulator.AddMultiplier(SkillStat.Damage, 2f, MultiplierTiming.Post);

    Assert.That(accumulator.Resolve(SkillStat.Damage, 10f), Is.EqualTo(60f).Within(0.0001f));
}
```

Same numeric result as the original test (`(10+5)*(1+1)*2 = 60`); `SkillStat.Damage`
here is just an arbitrary index into the accumulator's per-stat arrays, not a
claim about any support — no interface needed at all.

**`PreAndPostMultipliersDifferOnlyWithFlatAdds`** tested Pre vs. Post
multiplier timing — a real `StatModifierAccumulator` feature
([StatModifierAccumulator.cs:37-47](../../Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs#L37-L47))
that **no production support exercises today** (every real
`IMultiplierModifier`/`IManaModifiers.IMultiplierModifier` implementation
uses the sink's default `Post` timing; nothing passes `Pre` explicitly). This
was never really a "does some support work" test — same treatment, direct
accumulator unit test:

```csharp
[Test]
public void PreAndPostMultipliersDifferOnlyWithFlatAdds()
{
    var preAccumulator = new StatModifierAccumulator();
    preAccumulator.AddAdded(SkillStat.Damage, 10f);
    preAccumulator.AddMultiplier(SkillStat.Damage, 2f, MultiplierTiming.Pre);

    var postAccumulator = new StatModifierAccumulator();
    postAccumulator.AddAdded(SkillStat.Damage, 10f);
    postAccumulator.AddMultiplier(SkillStat.Damage, 2f, MultiplierTiming.Post);

    Assert.That(preAccumulator.Resolve(SkillStat.Damage, 5f), Is.EqualTo(20f).Within(0.0001f));
    Assert.That(postAccumulator.Resolve(SkillStat.Damage, 5f), Is.EqualTo(30f).Within(0.0001f));
}
```

Same numeric results as the original (`(5*2+10)=20` Pre; `(5+10)*2=30` Post).
This is also the only place in the test suite that would still cover
`MultiplierTiming.Pre` at all after this plan — worth keeping precisely
because no content support exercises it.

With both rewrites in place, `TestAddedSupport`/`TestIncreasedSupport`/
`TestMultiplierSupport` and their 3 factory methods have zero remaining
callers — delete them, don't leave them unused. This is also why
`IDamageModifiers` only needs `IBaseValueModifier` in the coverage table
above: nothing, production or test, needs the other two kinds once this
migration lands.

## Acceptance Criteria

- `ModifierKindInterfaces.cs` contains exactly the 7 stat-specific interface
  families from the coverage table (10 nested interfaces total — only
  `ManaCost` carries all 3 kinds; every other family carries only what a
  real production support uses) plus `IProjectileBehaviorModifier`/
  `IAoeBehaviorModifier`, unchanged. The old flat `IBaseValueModifier`/
  `IIncreasedModifier`/`IMultiplierModifier` no longer exist anywhere in the
  file.
- `grep`-ing the whole `Assets/` tree (including `Assets/Tests/`) for a bare
  (non-nested) `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier`
  reference returns nothing.
- `SkillSetCompiler.CollectSupportModifiers` has exactly 10 `is` checks, one
  per coverage-table row, each invoking the correct sink type.
- Every one of the 9 concrete supports compiles, implements the interfaces
  from the coverage table matching its own stat(s) plus whichever
  `IManaModifiers` interface(s) it needs, and produces numerically identical
  output to before this change for every field at its default value.
- `TestAddedSupport`/`TestIncreasedSupport`/`TestMultiplierSupport` and their
  3 factory methods no longer exist in `ModifierFoldEditModeTests.cs`.
  `AddedIncreasedAndMultiplierComposePerFormula` and
  `PreAndPostMultipliersDifferOnlyWithFlatAdds` still exist, still pass, and
  now call `StatModifierAccumulator` directly with the same numeric
  assertions as before — confirms the rewrite changed *how* the formula is
  exercised, not *what* it asserts.
- `FasterProjectilesSupport` in particular: confirm both `ProjectileSpeed`
  and `ProjectileLifetime` still resolve correctly with two separate
  explicit-interface methods, not silently dropped in the split.
- No support (production or test-only) declares a bare (non-nested)
  `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier` in its class
  declaration.

## Estimated Scope

Large for this plan (though still contained to one feature area) — full
rewrite of `ModifierKindInterfaces.cs` and `CollectSupportModifiers`, all 9
production support files migrating their primary interface in addition to
their mana one, plus deleting 6 now-obsolete test-only members and rewriting
2 tests in `ModifierFoldEditModeTests.cs`. Mechanical throughout (no new
runtime logic, no new fold behavior), but the file count and required
precision (explicit interface implementation everywhere two kind-shaped
interfaces coexist) make this the biggest single task in the plan.

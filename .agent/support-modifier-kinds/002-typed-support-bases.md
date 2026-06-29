# 002 — Modifier base + composable kind interfaces

Replace the single unconstrained `AdditiveSupport.Apply` surface with one SO base
that carries tags, plus one **interface per kind** that a support implements as
needed. A support may implement several (Piercing implements two). Depends on 001.

## `SkillSupport.cs` (modify)
Remove `RecoverySpeedMultiplier` — recovery becomes an ordinary increased modifier on
`SkillStat.RecoverySpeed`, so the bespoke property is dead.
```csharp
public abstract class SkillSupport : ScriptableObject { }
```

## New SO base: `StatModifierSupport.cs`
Common base for every augment support (numeric or behavior). Carries the tag
declaration the validator keys on (replaces the old `AdditiveSupport` check). Does
**not** itself declare any modifier API — that comes from the interfaces.
```csharp
public abstract class StatModifierSupport : SkillSupport
{
    public abstract SkillDefinitionTags SupportedSkillTags { get; }
}
```
`ConversionSupport` stays separate (not a `StatModifierSupport`, no tags), unchanged.

## Kind interfaces (the "impossible to misuse" boundary)
Each exposes only its kind's constrained sink/context. A support implements one or
more; the compiler dispatches by `is` checks. No base class per kind, so kinds
compose freely.

```csharp
// Kind 1 — base value (flat added)
public interface IBaseValueModifier  { void CollectAdded(AddedSink sink); }

// Kind 2 — additive (increased %)
public interface IIncreasedModifier  { void CollectIncreases(IncreasedSink sink); }

// Kind 3 — multiplier (Pre/Post "more")
public interface IMultiplierModifier { void CollectMultipliers(MultiplierSink sink); }

// Kind 4 — enable/disable / behavior, shape-typed
public interface IProjectileBehaviorModifier { void ApplyToProjectile(ProjectileBehaviorContext ctx); }
public interface IAoeBehaviorModifier        { void ApplyToAoe(AoeBehaviorContext ctx); }
```

The old `AdditiveSupport` class is **deleted**. There is no `Apply(SkillDefinition)`
anywhere. An `IIncreasedModifier` can only call `IncreasedSink.Add(stat, percent)` —
no path to a multiply or base-value write, even when the same support also implements
another interface (each interface receives a distinct sink).

## Why interfaces, not base classes
Piercing is both a base-value modifier (pierce count) and a behavior modifier
(repeat-hit cooldown). Single-inheritance base-class-per-kind cannot express that.
Interfaces give identical per-kind enforcement plus composition, and keep the SO
hierarchy flat (`StatModifierSupport` → concrete support). See index "Decision
updates".

## Acceptance criteria
- `SkillSupport` no longer declares `RecoverySpeedMultiplier`.
- No `AdditiveSupport` type and no `Apply(...)` method exist.
- Each kind interface exposes exactly one constrained entry point.
- Concrete supports do not compile yet (fixed in 003) — expected.

## Scope: small. New SO base + five small interfaces; delete old `AdditiveSupport`.

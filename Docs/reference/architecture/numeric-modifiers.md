# Numeric Modifiers

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

This is a general pattern, not a skill-system concept. Any numeric stat
modified from more than one source - a skill support, a player stat sheet, a
mob stat, an item, a status effect, anything with a base value that flat
bonuses, percent increases, and multipliers can stack onto - should resolve
through this same fold instead of hand-rolling its own math. The skill system
is the current concrete consumer; it is not the owner of the pattern.

## The Fold

`PlayGround.Common.Modifiers.StatFold.Resolve` is the one formula:

```csharp
public static class StatFold
{
    public static float Resolve(float baseValue, float added, float increasedPercent, float multiplier)
    {
        float effectiveBase = baseValue + added;
        return effectiveBase * (1f + increasedPercent) * multiplier;
    }
}
```

```text
value = (base + added) * (1 + increased) * multiplier
```

It is a pure function with no dependency on skills, players, or any other
domain type - `Assets/Scripts/Common/Modifiers/StatFold.cs`. Any system with a
numeric value that needs to combine a base with flat bonuses, summed percent
increases, and multipliers calls this directly rather than writing the
expression inline. Flat `added` amounts join the base before `increased` and
`multiplier` are applied, so both scale the combined total, not just the raw
base.

## Combining Multiple Sources

A single value often has more than one contributor - several supports on a
skill, several stat sources on a player. `added` and `increased` amounts are
summed across contributors; multipliers are multiplied across contributors.
Combine order never changes the numeric result, so contribution order is not
meaningful and does not need to be authored or preserved.

The skill system's `StatModifierAccumulator`
(`Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs`) is the current
concrete example of this: it accumulates `added`/`increased`/`multiplier`
totals per `SkillStat` from every contributing source, then calls
`StatFold.Resolve` once per stat. Any other domain that needs to combine
several sources into one numeric value before folding can follow the same
shape - a small per-domain accumulator keyed by whatever the domain's stats
are, backed by the same `StatFold.Resolve` call - rather than inventing a new
fold formula.

## Single-Source Resolves

Not every numeric modifier needs an accumulator. A value with only one
contributing source (or no notion of "many supports contributing to one set")
can call `StatFold.Resolve` directly. The skill system does this for two
per-edge trigger-link concerns that fall outside the per-skill accumulator:

- `TriggerLink.ResolveManaCostFactor()` resolves a link's own mana-cost
  factor from its `manaCostIncreased` and `manaCostMultiplier` fields
  (`base = 1`, `added = 0`).
- `IntervalSpawnTrigger.ResolveEnergyPerSecond(snapshot)` resolves the
  trigger's authored `energyPerSecond` against the player stat sheet's
  `baseEnergyGain` / `increasedEnergyGainPercent` / `energyGainMultiplier`
  terms.

See [skill-modifiers.md](../game-logic/skill-modifiers.md) for the skill
system's full application of this pattern: the modifier kind interfaces,
`SkillStat`, the current augment support roster, and player-level stat
resolution.

---
name: energy-gain-docs-update
description: Document the new player energy-gain modifiers in skill-system.md
---

# 004 — Energy Gain Docs Update

## Scope

Update `Docs/reference/game-logic/skill-system.md` to describe the new
player-level energy-gain modifiers and how they fold into interval-trigger
`EnergyPerSecond`. Depends on 001 and 002 for final field names/formula.

## Changes

### `SkillStatSnapshot` fields list (Layer 1.5 section)

At [skill-system.md:129-133](../../Docs/reference/game-logic/skill-system.md#L129-L133),
add the 3 new fields to the list:

```
- `baseEnergyGain`
- `increasedEnergyGainPercent`
- `energyGainMultiplier`
```

### Baking rules (same section)

At [skill-system.md:134-138](../../Docs/reference/game-logic/skill-system.md#L134-L138),
add a bullet after the existing three, noting this one does **not** bake
through the per-skill accumulator like the others:

```
- `baseEnergyGain`, `increasedEnergyGainPercent`, and `energyGainMultiplier`
  do not feed the per-skill `StatModifierAccumulator` fold used by Rate/
  Damage/AreaSize. They fold directly into each interval trigger's own
  `energyPerSecond` at compile time (see Interval Trigger section below) —
  an interval trigger is a per-edge concern with its own base value, not a
  set-level stat any support can attach to.
```

### Interval Trigger section

At [skill-system.md:630-634](../../Docs/reference/game-logic/skill-system.md#L630-L634)
(the paragraph beginning "Both concrete interval triggers inherit
`energyPerSecond`..."), add a new paragraph directly after it:

```
The compiled `EnergyPerSecond` is not the trigger's raw authored value.
`IntervalSpawnTrigger.ResolveEnergyPerSecond` folds it against the player
stat sheet's energy-gain terms:

resolvedEnergyPerSecond = max(0.01, (energyPerSecond + baseEnergyGain)
                               * (1 + increasedEnergyGainPercent)
                               * energyGainMultiplier)

`baseEnergyGain`, `increasedEnergyGainPercent`, and `energyGainMultiplier`
are authored on `UnitStatSheet` and reach every interval trigger through the
same `SkillStatSnapshot` used for Rate/Damage/AreaSize. Unlike
`TriggerLink`'s mana-cost factor — which only ever scales an already-resolved
child cost with no base of its own — energy gain has a genuine base (the
trigger's own authored `energyPerSecond`), so it folds through the same
base/added/increased/multiplier shape as Rate, just resolved locally on
`IntervalSpawnTrigger` rather than through the per-skill accumulator.
```

## Acceptance Criteria

- Doc changes accurately reflect the shipped field names and formula from
  tasks 001-002 (verify against actual code, not this task's draft text, in
  case naming shifted during implementation).
- No changes to the "Legacy" sections or any other part of the doc.
- Markdown renders correctly (fenced code block for the formula, consistent
  with the doc's existing style for other fold formulas at
  [skill-system.md:483-488](../../Docs/reference/game-logic/skill-system.md#L483-L488)).

## Dependencies

Depends on 001 and 002 (needs final shipped names/formula, not the draft in
this file).

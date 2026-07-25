# 002 — Supports contribute mana cost

## Goal
Let supports raise a skill's mana cost, so heavier child sets (more projectiles,
piercing, homing) cost more energy per spawned child. Reuse the existing
`IBaseValueModifier` sink pattern (same shape as `AddedDamageSupport`).

## Design
Each participating support gets a serialized `[SerializeField, Min(0f)] float
manaCostAdded` and implements (or extends its existing) `IBaseValueModifier` to
`sink.Add(SkillStat.ManaCost, manaCostAdded)`. Flat added value keeps it trivial
to author and balance; can switch to increased-% later if desired (open question
2 in index).

## Changes
Named in the request first, then the natural siblings:

1. **`MultipleProjectilesSupport.cs`** — add `IBaseValueModifier`; add
   `manaCostAdded` (default e.g. `4f`); `CollectAdded` adds to `SkillStat.ManaCost`.
2. **`PiercingSupport.cs`** — already implements `IBaseValueModifier` (adds
   `PierceCount`). Add `manaCostAdded` (default e.g. `3f`) to the existing
   `CollectAdded`.
3. **`HomingSupport.cs`** — add `IBaseValueModifier`; add `manaCostAdded`
   (default e.g. `3f`); implement `CollectAdded`.
4. **`MultipleAoesSupport.cs`** — add `IBaseValueModifier` + `manaCostAdded`
   (parity with Multiple Projectiles for the AOE side).
5. **`AddedDamageSupport.cs`** — optional: add `manaCostAdded` so a raw damage
   support can also carry cost. Default `0f` to stay behavior-neutral unless
   authored.

Defaults are illustrative and unbalanced by design ("just to check things work").

## Acceptance Criteria
- A child set with Multiple Projectiles / Piercing / Homing compiles to a higher
  `RuntimeProjectileDefinition.ManaCost` than the bare skill, by exactly the sum
  of the supports' `manaCostAdded`.
- Removing the supports returns `ManaCost` to the authored base.
- No behavior change for supports whose `manaCostAdded` is left at `0`.

## Dependencies
Depends on 001 (`SkillStat.ManaCost`). Pairs with 003 to make the effect visible
(higher mana cost → higher energy threshold → slower child cadence).

## Scope
Small.

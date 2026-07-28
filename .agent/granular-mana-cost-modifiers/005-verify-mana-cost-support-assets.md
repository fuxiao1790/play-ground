---
name: verify-mana-cost-support-assets
description: User/editor step — spot-check existing support assets still compile/display correctly, optionally author new increased%/multiplier values
---

# 005 — Verify Mana Cost Support Assets (User/Editor Step)

## Depends On

001 (`IManaModifiers` interfaces and updated supports must exist and
compile).

## Scope

This is an in-editor verification step for the user, not code. Per project
convention, Unity asset inspection/authoring is done in the editor, not by
hand-reading `.asset` YAML.

Lower-stakes than earlier plan drafts: this design never moves a field to a
different declaring class, so there's no serialization-migration risk to
chase down — `manaCostAdded` on the 5 pre-existing supports stays exactly
where it was.

## Steps (for the user, in the Unity editor)

1. Open one existing `.asset` instance of `PiercingSupport` or
   `AddedDamageSupport` under `Assets/ScriptableObjects/Skills/Supports/`
   ([skill-system.md:922-925](../../Docs/reference/game-logic/skill-system.md#L922-L925))
   and confirm both fields (own-stat and `manaCostAdded`) still show their
   previously authored values and both still apply correctly in a compiled
   skill set — a quick real-world check that the explicit-interface split
   didn't silently drop one of the two contributions.
2. Since task 001 also touches every support's *primary* contribution (not
   just mana), spot-check one in-scene loadout that uses `FasterProjectilesSupport`
   (the one support with 3 explicit interface methods after this change) and
   confirm both `ProjectileSpeed` and `ProjectileLifetime` still visibly
   scale correctly in play mode — the clearest real-world check that
   splitting its one old method into two per-stat methods didn't drop either
   value.
3. Optionally: set the new `manaCostIncreasedPercent` field on any
   `IncreasedAoeSupport`/`IncreasedRateSupport` asset, or the new
   `manaCostMultiplier` field on any `ConcentratedEffectSupport`/
   `FasterProjectilesSupport` asset, to try the new granularity — e.g. give
   `ConcentratedEffectSupport` a `manaCostMultiplier` of `0.8` as a "cheaper
   but smaller" tradeoff support.

## Acceptance Criteria

- At least one dual-purpose support (`PiercingSupport` or
  `AddedDamageSupport`) is spot-checked in the inspector and confirmed to
  still apply both its own-stat and mana-cost contributions correctly.
- `FasterProjectilesSupport` specifically confirmed in play mode: both
  `ProjectileSpeed` and `ProjectileLifetime` still scale as authored.
- No `.asset` file is hand-edited as text — inspected and, if desired,
  changed entirely through the Unity inspector.

## Estimated Scope

User-owned editor time — not implementation work.

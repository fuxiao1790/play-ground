---
name: mana-cost-docs-update
description: Rewrite the Supports section's interface documentation for the 7 per-stat interface families, plus the trigger increased-percent fold
---

# 004 — Mana Cost Docs Update

## Depends On

001, 002 (docs must describe the shipped shape, not the planned one).

## Scope

Docs are design references, kept accurate per repo convention
([skill-system.md:3-4](../../Docs/reference/game-logic/skill-system.md#L3-L4)).
This grew alongside task 001 — the interface code block in `skill-system.md`
documents the *exact* structure task 001 replaces, so it needs a full
rewrite, not a paragraph appended after it.

## Files

### `Docs/reference/game-logic/skill-system.md`

- **The kind-interface code block**
  ([skill-system.md:375-409](../../Docs/reference/game-logic/skill-system.md#L375-L409)):
  replace the `IBaseValueModifier`/`IIncreasedModifier`/`IMultiplierModifier`
  block with the 7 per-stat families task 001 ships (`IDamageModifiers`,
  `IAreaSizeModifiers`, `IProjectileSpeedModifiers`,
  `IProjectileLifetimeModifiers`, `IRateModifiers`, `IPierceCountModifiers`,
  `IManaModifiers`), each showing only the kind(s) it actually declares (see
  task 001's coverage table). Keep `IProjectileBehaviorModifier`/
  `IAoeBehaviorModifier` as-is at the end of the block — unaffected.
- **The "may implement more than one kind" paragraph**
  ([skill-system.md:411-415](../../Docs/reference/game-logic/skill-system.md#L411-L415)):
  currently says "`PiercingSupport` is both an `IBaseValueModifier` for
  `PierceCount` and an `IProjectileBehaviorModifier` for
  `RepeatHitCooldown`." Update to name the new qualified interface
  (`IPierceCountModifiers.IBaseValueModifier`), and add a sentence on
  explicit interface implementation: a support implementing two
  identically-shaped interfaces (e.g. `PiercingSupport` also implementing
  `IManaModifiers.IBaseValueModifier`) must give each an explicit
  implementation, since one implicit method can't serve both with different
  bodies.
- **"Current augment supports" table**
  ([skill-system.md:438-451](../../Docs/reference/game-logic/skill-system.md#L438-L451)):
  update every row's "Kind interface(s)" column to the new qualified names
  (e.g. `Piercing` → `IPierceCountModifiers.IBaseValueModifier`,
  `IManaModifiers.IBaseValueModifier`, `IProjectileBehaviorModifier`).
  Update the "Stat / behavior contribution" column: the existing "Adds
  `ManaCost`" phrasing on `Multiple Projectiles`/`Piercing`/`Homing`/
  `Multiple AOEs`/`Added Damage` stays accurate (same field, now via
  `IManaModifiers.IBaseValueModifier`). Add the same kind of note to
  `Increased AOE Effect`/`Increased Skill Speed` ("also increases
  `ManaCost`" via the new `manaCostIncreasedPercent` field) and to
  `Concentrated Effect`/`Faster Projectiles` ("also multiplies `ManaCost`"
  via the new `manaCostMultiplier` field).
- **The `manaCost` paragraph**
  ([skill-system.md:272-289](../../Docs/reference/game-logic/skill-system.md#L272-L289)):
  currently says "Every `TriggerLink` has one `manaCostMultiplier`." Update to
  describe the two-field fold: `manaCostIncreasedPercent` and
  `manaCostMultiplier`, resolved as
  `(1 + manaCostIncreasedPercent) * manaCostMultiplier` per link (mirror the
  formula from task 002's `TriggerLink.ResolveManaCostFactor`). Keep the
  worked example (`A * multiplier1 * multiplier2 + ...`) but note each
  `multiplierN` here now means "that link's resolved factor," not the raw
  field.

### `Docs/flows/resource-spend-gate.md`

- Step 1 ([resource-spend-gate.md:6-9](../../Docs/flows/resource-spend-gate.md#L6-L9))
  currently says "Each valid link's `manaCostMultiplier` multiplies both the
  initial active skill chain cost and that link's triggered skill cost."
  Update to say each link's *resolved factor* (`(1 + increasedPercent) *
  multiplier`) does this, referencing `TriggerLink.ResolveManaCostFactor`.

## Acceptance Criteria

- The interface code block in `skill-system.md` matches task 001's shipped
  `ModifierKindInterfaces.cs` exactly — same 7 family names, same kinds
  declared per family, same `IProjectileBehaviorModifier`/
  `IAoeBehaviorModifier` tail.
- No remaining doc reference to a bare (non-nested) `IBaseValueModifier`/
  `IIncreasedModifier`/`IMultiplierModifier` anywhere in `skill-system.md`.
- The augment-supports table's "Kind interface(s)" column uses fully
  qualified names for every row.
- No leftover references to abstract base classes or dedicated SO types from
  earlier plan drafts that were never built.
- No other doc changes — scope is limited to the sections above; don't
  restructure surrounding material (e.g. the fold-formula explanation at
  [skill-system.md:417-421](../../Docs/reference/game-logic/skill-system.md#L417-L421)
  stays as-is, it's still accurate).

## Estimated Scope

Medium — the interface code block and the augment-supports table both need a
real rewrite (not just an added paragraph) to stay accurate against task
001's shipped interface names, on top of the smaller trigger-formula edits.

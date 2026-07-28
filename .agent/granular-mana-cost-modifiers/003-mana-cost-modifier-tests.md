---
name: mana-cost-modifier-tests
description: EditMode coverage for the IManaModifiers interface family and the trigger increased-percent formula
---

# 003 — Mana Cost Modifier Tests

## Depends On

001 (the full per-stat interface reorganization — `IManaModifiers` plus the
6 other stat-specific families, 10 compiler checks, all 9 supports migrated,
`ModifierFoldEditModeTests.cs`'s obsolete test-only helpers deleted and its 2
affected tests rewritten against `StatModifierAccumulator` directly — must be
complete and compiling), 002 (`manaCostIncreasedPercent` /
`ResolveManaCostFactor` on `TriggerLink` must exist).

Note: getting `ModifierFoldEditModeTests.cs` compiling and passing again is
task 001's responsibility (it's a required companion change, not new
coverage). This task only adds *new* tests.

## Scope

Add EditMode tests next to the existing mana-cost tests in
`Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
([SkillValidationEditModeTests.cs:89-146](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L89-L146),
[:202-251](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L202-L251)
are the existing precedent for shape/helpers: `CreateAsset<T>`,
`CreateSkillSet`, `SkillLoadoutNode`).

## Tests To Add

1. **`CompilerAppliesManaCostAddedAlongsideOwnStat`** — one skill with
   authored `manaCost = 10`, one `PiercingSupport` with `pierceCount = 2` and
   `manaCostAdded = 3`. Assert compiled `ManaCost == 13` **and**
   `PierceCount == 2` — confirms `IBaseValueModifier.CollectAdded` (own stat)
   and `IManaModifiers.IBaseValueModifier.CollectAdded` (mana) both fire
   correctly as separate explicit-interface implementations on the same
   class, with neither shadowing the other.
2. **`CompilerAppliesManaCostIncreasedPercentAlongsideOwnStat`** — same
   skill, one `IncreasedAoeSupport` with `areaSizeMultiplier = 1.5` and (new
   field) `manaCostIncreasedPercent = 0.5`. Assert compiled `ManaCost == 15`
   (`10 * (1 + 0.5)`) **and** `AreaSize` reflects `areaSizeMultiplier` as
   before — same explicit-interface-pair check, for `IIncreasedModifier`.
3. **`CompilerAppliesManaCostMultiplierAlongsideOwnStat`** — same skill, one
   `ConcentratedEffectSupport` with `areaSizeMultiplier = 0.75` and (new
   field) `manaCostMultiplier = 1.5`. Assert compiled `ManaCost == 15`
   (`10 * 1.5`) **and** `AreaSize` reflects `areaSizeMultiplier` — same check
   for `IMultiplierModifier`.
4. **`CompilerCombinesManaCostFoldOrderAcrossSupports`** — same skill, three
   supports on one set: `MultipleProjectilesSupport` (`manaCostAdded = 3`,
   unchanged default field), `IncreasedAoeSupport`
   (`manaCostIncreasedPercent = 0.5`), `ConcentratedEffectSupport`
   (`manaCostMultiplier = 1.5`). Assert full fold order:
   `(10 + 3) * (1 + 0.5) * 1.5 == 29.25`. Stat modifiers apply regardless of
   `SupportedSkillTags` mismatch (only `IProjectileBehaviorModifier`/
   `IAoeBehaviorModifier` application is tag-gated via the definition-type
   check in `ApplySupportBehaviors`), so combining Projectile- and
   Aoe-tagged supports on one set is valid for this test.
5. **`CompilerLeavesManaCostUnchangedForSupportsWithoutManaModifiers`** — put
   a support that implements no `IManaModifiers` interface at all (any
   support before this feature existed would qualify, but concretely:
   confirm a set with only behavior-only aspects, e.g. checking that a
   skill's own `ApplyToProjectile` effects like `RepeatHitCooldown` apply
   with `ManaCost` unaffected) resolves `ManaCost` to exactly the skill's
   authored value — regression guard that the new compiler checks are true
   no-ops for anything that doesn't opt in.
6. **`CompilerAppliesTriggerManaCostIncreasedPercent`** — mirror
   `CompilerAddsChainedTriggeredManaCostsToActiveSkill`
   ([SkillValidationEditModeTests.cs:113-146](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L113-L146))
   with one trigger link only, `manaCostMultiplier = 2f,
   manaCostIncreasedPercent = 0.5f`, child `manaCost = 4`. Assert the active
   skill's total includes `4 * (1 + 0.5) * 2 == 12` for that triggered leg
   (not `4 * 2 == 8`).
7. **`CompilerAppliesIntervalManaCostIncreasedPercent`** — mirror
   `CompilerAppliesIntervalManaCostMultiplier`
   ([SkillValidationEditModeTests.cs:229-251](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L229-L251))
   with `manaCostMultiplier = 2f, manaCostIncreasedPercent = 0.5f`, child
   `manaCost = 4f`. Assert `setup.EnergyThreshold == 12f` (`4 * 1.5 * 2`).

## Acceptance Criteria

- All 7 new tests pass.
- All pre-existing tests in `SkillValidationEditModeTests.cs` **and**
  `ModifierFoldEditModeTests.cs` still pass — the latter's 2 rewritten tests
  (task 001) assert the exact same numeric results as before, just against
  `StatModifierAccumulator` directly instead of through deleted test-only
  supports — confirming no numeric regression from the interface
  reorganization anywhere in the skill-modifier system.
- Run via Unity Test Runner (EditMode) or `dotnet test`/CLI batchmode per
  [Docs/testing.md](../../Docs/testing.md), whichever this repo's existing
  workflow uses — match however the existing suite is normally run, don't
  introduce a new test-running mechanism.

## Estimated Scope

Small — 7 focused unit tests following existing patterns in the same file, no
new test infrastructure.

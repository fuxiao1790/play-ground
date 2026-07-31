---
name: player-energy-gain-modifiers
description: Add base/increased/multiplying energy-gain modifiers to the player stat sheet, folded into interval-spawn energy accrual
---

# Player Energy Gain Modifiers

## Summary

User ask: "player stat sheet should have base, increased, multiplying energy
gain modifiers that is used for interval spawn."

Today `IntervalSpawnTrigger.energyPerSecond`
([IntervalSpawnTrigger.cs:8](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L8))
is a raw per-link authored float, baked unmodified into
`RuntimeChildSpawnSetup.EnergyPerSecond` /
`RuntimeAoeIntervalSpawnSetup.EnergyPerSecond` at both interval-trigger
compile sites
([SkillSetCompiler.cs:395](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L395),
[SkillSetCompiler.cs:437](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L437)),
clamped only to a 0.01 floor. No player-level stat touches it at all —
`SkillStatSnapshot` currently only carries `IncreasedRatePercent`,
`DamageMultiplier`, `CritChance`, `CritMultiplier`, `AreaSizeMultiplier`
([SkillStatSnapshot.cs:9-27](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L9-L27)),
sourced from `UnitStatSheet`
([UnitStatSheet.cs:17-34](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L17-L34)).
`UnitStatSheet` is the type referred to as "player stat sheet" — it is
assigned on both `PlayerRoot`
([PlayerRoot.cs:31](../../Assets/Scripts/Player/PlayerRoot.cs#L31)) and
`SkillDriver`
([SkillDriver.cs:32](../../Assets/Scripts/Skills/SkillDriver.cs#L32),
also used by `MobRoot`), and `SkillDriver` is what actually calls
`SkillStatAggregator.Aggregate(runtimeLoadout, statSheet)`
([SkillDriver.cs:194](../../Assets/Scripts/Skills/SkillDriver.cs#L194)).

This plan adds three new `UnitStatSheet` fields — a flat base add, an
increased percent, and a post multiplier — and folds them into every
compiled interval trigger's `EnergyPerSecond` through the same
`SkillStatSnapshot` plumbing already used for Rate/Damage/AreaSize, without
introducing a new `SkillStat` enum member or touching
`StatModifierAccumulator`.

## Constraints & Invariants

- **Existing fold shape**: `value = (base * preMultiplier + added) * (1 +
  increased) * postMultiplier`, documented at
  [skill-system.md:468-471](../../Docs/reference/game-logic/skill-system.md#L468-L471)
  and implemented in `StatModifierAccumulator.Resolve`
  ([StatModifierAccumulator.cs:49-54](../../Assets/Scripts/Skills/Modifiers/StatModifierAccumulator.cs#L49-L54)).
  The new energy-gain calculation must read as the same shape (no preMul term
  needed since nothing produces one), not invent a differently-ordered
  formula.
- **Trigger-edge values are per-edge, not set-level stats.** `energyPerSecond`
  lives on `IntervalSpawnTrigger`
  ([IntervalSpawnTrigger.cs:8](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L8)),
  not on any `SkillSet`/`Skill`/support. Nothing in the support system
  (`StatModifierSupport` and its kind interfaces,
  [skill-system.md:381-457](../../Docs/reference/game-logic/skill-system.md#L381-L457))
  can ever attach to a trigger link — supports only modify "the Skill in the
  same set"
  ([skill-system.md:1199](../../Docs/reference/game-logic/skill-system.md#L1199)).
  This mirrors the already-decided trigger-side mana-cost asymmetry
  (`TriggerLink.ResolveManaCostFactor`,
  [TriggerLink.cs:49-50](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs#L49-L50)) —
  a per-edge scalar computed locally on the trigger type, not through
  `StatModifierAccumulator`/`SkillStat`.
- **`snapshot` is already threaded to both compile sites.** `ApplyChildSpawn`
  and `ApplyAoeIntervalSpawn` both already receive `SkillStatSnapshot snapshot`
  as a parameter
  ([SkillSetCompiler.cs:373-378](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L373-L378),
  [:415-420](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L415-L420)).
  No new parameter plumbing is needed anywhere in the compile call chain.
- **Backward compatibility.** Every existing `SkillStatSnapshot.Identity`-based
  test must keep asserting the same `EnergyPerSecond` values —
  `SkillValidationEditModeTests.CompilerMapsProjectileEnergyRateAndCost`
  ([SkillValidationEditModeTests.cs:279-301](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L279-L301))
  and the AOE-interval sibling test
  ([:303-330](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L303-L330))
  assert exact `EnergyPerSecond` equal to the raw authored
  `trigger.energyPerSecond`. New fields must default to neutral (`0` base,
  `0%` increased, `x1` multiplier) so `(v + 0) * (1+0) * 1 == v`.
- **Constructor call-site safety.** `SkillStatSnapshot`'s constructor is called
  positionally and with named args across 4+ test files (`ModifierFoldEditModeTests.cs`,
  `CritEditModeTests.cs`); new parameters must be appended at the end with
  defaults matching `Identity`'s neutral values so no existing call site needs
  editing.
- **`UnitStatSheet` is shared by Player and Mob.** Both `PlayerRoot` and
  `MobRoot` assign a `UnitStatSheet`
  ([PlayerRoot.cs:31](../../Assets/Scripts/Player/PlayerRoot.cs#L31),
  [MobRoot.cs:29](../../Assets/Scripts/Mob/MobRoot.cs#L29)), and
  `MobRoot.ConfigureAuthoring` cold-creates one at runtime via
  `ScriptableObject.CreateInstance<UnitStatSheet>()`
  ([MobRoot.cs:177](../../Assets/Scripts/Mob/MobRoot.cs#L177)) without calling
  `SetRuntimeValues` for the new fields — new properties must have safe
  zero/neutral defaults on an uninitialized instance (Unity's default field
  values), not rely on any constructor.
- **ScriptableObject asset authoring is a user/editor step**, not something to
  hand-edit as YAML — same convention as every other support/stat-sheet change
  in this repo ([[editor-steps-are-user-steps]],
  [skill-system.md:920-925](../../Docs/reference/game-logic/skill-system.md#L920-L925)).

## Mechanisms Reused vs. Introduced

Reused:
- `SkillStatSnapshot` / `SkillStatAggregator.Aggregate` — the existing
  single-hop pipeline from `UnitStatSheet` to a flat snapshot
  ([SkillStatSnapshot.cs:30-41](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L30-L41)).
  Grown by 3 fields, same shape.
- `snapshot` parameter already carried into `ApplyChildSpawn` /
  `ApplyAoeIntervalSpawn` — zero new plumbing.
- The `(base + added) * (1+increased) * postMultiplier` fold shape, applied
  inline rather than through `StatModifierAccumulator` (see invariant above
  on why this is per-edge, not per-stat).
- `TriggerLink.ResolveManaCostFactor()`'s existing precedent of a small
  resolver method living directly on the trigger type
  ([TriggerLink.cs:49-50](../../Assets/Scripts/Skills/Trigger/TriggerLink.cs#L49-L50)) —
  `IntervalSpawnTrigger.ResolveEnergyPerSecond(snapshot)` follows the same
  pattern one level down the inheritance chain.

Introduced:
- 3 new `UnitStatSheet` fields: `baseEnergyGain`, `increasedEnergyGainPercent`,
  `energyGainMultiplier`, with public accessor properties matching the
  existing `IncreasedRatePercent`-style clamp/scale convention
  ([UnitStatSheet.cs:30](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L30)).
- 3 new `SkillStatSnapshot` fields carrying those through, as trailing
  constructor parameters with neutral defaults.
- 1 new method: `IntervalSpawnTrigger.ResolveEnergyPerSecond(SkillStatSnapshot)`.
  Justification: the resolved value is used at both interval-trigger compile
  sites, so a shared method on the trigger avoids duplicating the fold
  formula twice in `SkillSetCompiler`.

Nothing removed.

## Design Validation

- *Fold shape preserved*: `ResolveEnergyPerSecond` computes
  `(energyPerSecond + snapshot.BaseEnergyGain) * (1 +
  snapshot.IncreasedEnergyGainPercent) * snapshot.EnergyGainMultiplier`,
  floored to `0.01` exactly like today — same `(base+added)*(1+increased)*postMul`
  shape as every other stat, just evaluated locally instead of through
  `StatModifierAccumulator`.
- *Per-edge scoping respected*: no `SkillStat` enum member added, no
  `StatModifierAccumulator` change, no support-side interface added — nothing
  claims a support can modify energy gain, which is correct since no support
  lives on a `TriggerLink`.
- *Backward compatible*: with `SkillStatSnapshot.Identity` (new fields default
  `0`, `0`, `1`), `ResolveEnergyPerSecond` reduces to
  `Mathf.Max(0.01f, energyPerSecond)` — byte-for-byte the current formula.
  Both existing `EnergyPerSecond` assertions in
  `SkillValidationEditModeTests.cs` keep passing unmodified.
- *Mob safety*: `UnitStatSheet` instances created via
  `ScriptableObject.CreateInstance` (mob runtime path) get C#/Unity default
  field values (`0f` for all three new floats). `EnergyGainMultiplier`'s
  accessor is `Mathf.Max(0f, energyGainMultiplier)`, which would resolve a
  *zero* multiplier for a never-configured mob stat sheet, driving
  `EnergyPerSecond` to the 0.01 floor instead of the trigger's authored value.
  This is caught below and fixed by defaulting the *field* to `1f`, not just
  documenting the property — same pattern `damageMultiplier`/`areaSizeMultiplier`
  already use (`= 1f` field initializer,
  [UnitStatSheet.cs:20](../../Assets/Scripts/Common/Stats/UnitStatSheet.cs#L20)).

## Minimal/Additive vs. Refactor Comparison

- **Additive approach (chosen): 3 new `UnitStatSheet`/`SkillStatSnapshot`
  fields + 1 new `IntervalSpawnTrigger` method, fold computed inline at the
  2 existing compile call sites.**
  - Resulting data flow: `UnitStatSheet` → `SkillStatAggregator.Aggregate` →
    `SkillStatSnapshot` → `ApplyChildSpawn`/`ApplyAoeIntervalSpawn` (already
    receive `snapshot`) → `IntervalSpawnTrigger.ResolveEnergyPerSecond` →
    `RuntimeChildSpawnSetup`/`RuntimeAoeIntervalSpawnSetup.EnergyPerSecond`.
    Every hop already exists except the last resolver call.
  - New concepts/types introduced: none — 3 fields on 2 existing structs/SOs,
    1 method on an existing type.
  - Copies/translations added: none.
  - Long-term cost: none identified — this is exactly the shape
    `TriggerLink.manaCostMultiplier`/`manaCostIncreasedPercent` already
    established for per-edge scalars.

- **Refactor approach (rejected): add `SkillStat.EnergyGain` and route through
  `StatModifierAccumulator`.**
  - Resulting data flow: would require building/threading a
    `StatModifierAccumulator` instance at the trigger-compile call sites
    (currently only `CompileDefinition` builds one, for the *parent* set's
    own stats — the trigger's `energyPerSecond` has no relationship to that
    accumulator instance).
  - Existing concepts/types changed: `SkillStat` enum grows by a case that no
    support can ever produce (no `IEnergyGainModifiers` interface family is
    being requested), which is dead surface — every other `SkillStat` case
    exists because at least one production support implements a matching kind
    interface.
  - Copies/translations removed: none — this doesn't collapse any existing
    duplication, since energy gain isn't currently computed two ways anywhere.
  - Long-term cost: a permanently-unused enum case, plus an
    `StatModifierAccumulator` array slot (`StatCount` grows) that never
    receives support contributions, muddying "does anything modify
    `SkillStat.EnergyGain`?" for a future reader the same way the mana-cost
    plan explicitly avoided for trigger-edge mana math
    ([granular-mana-cost-modifiers/index.md:131-142](../granular-mana-cost-modifiers/index.md#L131-L142)).
  - Decision: **rejected.** Reason: energy gain is a per-edge property with no
    support-side producer, exactly like the already-settled mana-cost-factor
    precedent; forcing it through the set-level accumulator would be
    "duplicate ownership of the same responsibility" in the opposite
    direction (borrowing set-level machinery for an edge-level concern).

## Default Decision Rule Applied

`RuntimeChildSpawnSetup.EnergyPerSecond` / `RuntimeAoeIntervalSpawnSetup.EnergyPerSecond`
remain the single source of truth for a compiled trigger's resolved energy
rate — this plan only changes *what feeds* that one value at compile time,
same as every other stat fold in this system. No second representation of
energy gain is introduced anywhere (ECS-side `TimedSpawnComponent.EnergyPerSecond`
still just copies the already-resolved value, unchanged).

## Tasks

1. [001-player-energy-gain-stat-fields.md](001-player-energy-gain-stat-fields.md) —
   `UnitStatSheet` gains `baseEnergyGain`/`increasedEnergyGainPercent`/`energyGainMultiplier`;
   `SkillStatSnapshot` gains matching fields with neutral defaults;
   `SkillStatAggregator.Aggregate` passes them through.
2. [002-interval-trigger-energy-gain-fold.md](002-interval-trigger-energy-gain-fold.md) —
   `IntervalSpawnTrigger.ResolveEnergyPerSecond(SkillStatSnapshot)`; update both
   `SkillSetCompiler` call sites to use it instead of the raw
   `Mathf.Max(0.01f, trigger.energyPerSecond)`.
3. [003-energy-gain-modifier-tests.md](003-energy-gain-modifier-tests.md) —
   EditMode coverage: `UnitStatSheet` → snapshot conversion, and the resolved
   `EnergyPerSecond` fold at both interval-trigger compile sites, including a
   default/Identity regression check.
4. [004-energy-gain-docs-update.md](004-energy-gain-docs-update.md) — update
   `Docs/reference/game-logic/skill-system.md`'s `SkillStatSnapshot` fields
   list/baking section and the interval-trigger energy section.
5. [005-verify-energy-gain-stat-sheet-assets.md](005-verify-energy-gain-stat-sheet-assets.md) —
   user/editor step: confirm existing `UnitStatSheet` assets still compile
   interval triggers to identical `EnergyPerSecond` values, optionally author
   non-neutral values on the player's sheet.

Dependency order: 001 blocks 002. 003 and 004 each depend on both 001 and
002. 005 depends only on 001 and is a user-owned editor step, not code.

## Open Questions / Notes

- The `SkillStatSnapshot` fields list in
  [skill-system.md:129-133](../../Docs/reference/game-logic/skill-system.md#L129-L133)
  is already stale today (missing `critChance`/`critMultiplier`, which exist
  in code — [SkillStatSnapshot.cs:23-27](../../Assets/Scripts/Skills/SkillStatSnapshot.cs#L23-L27)).
  Task 004 adds the 3 new energy-gain fields to that list but does not attempt
  to backfill the pre-existing crit-field gap — that's a separate, unrelated
  doc-drift fix, not part of this ask.
- Exact default authored values for a non-neutral player build (how much
  `baseEnergyGain`/`increasedEnergyGainPercent`/`energyGainMultiplier` should
  the player actually start with, or gain from leveling) are content-balance
  choices, not architecture — task 001 defaults everything to neutral, task
  005 leaves tuning to the user in the inspector.
- This plan does not touch `TriggerLink.manaCostMultiplier`/mana-to-energy
  conversion (`IntervalSpawnTrigger.ManaToEnergyCost`,
  [IntervalSpawnTrigger.cs:10-11](../../Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs#L10-L11)) —
  that resolves `EnergyThreshold` (how much energy one child costs), a
  completely separate axis from `EnergyPerSecond` (how fast energy accrues),
  and the user's ask was specifically about gain/accrual.

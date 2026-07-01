# Rate-Based Recovery Rework

## Summary

Replace the exposed **recovery-time / recovery-speed** authoring vocabulary with an
exposed **rate** (attacks/casts per second). Increases scale the base rate as a
summed *increased* percentage. Cooldown and cooldown recovery become internal
derivations (`recoveryTime = 1 / rate`), no longer part of the authoring or stat
surface.

Concretely:

- `Skill.baseRecoveryTime` (seconds) → `Skill.baseRate` (casts/sec).
- `SkillStat.RecoverySpeed` → `SkillStat.Rate`.
- `IncreasedRecoverySpeedSupport` → `IncreasedRateSupport`, authored as an
  *increased percent* on `Rate` (menu label "Increased Skill Speed").
- Player-level `PlayerStatSnapshot.CastSpeedMultiplier` → `IncreasedRatePercent`,
  contributed to the **increased** bucket of `Rate` (not a multiplier).
- The bespoke recovery formula in `SkillSetCompiler` collapses to
  `rate = Resolve(Rate, baseRate)` (the generic fold) then `recoveryTime = 1 / rate`.
- `RuntimeSkillDefinition.RecoveryTime` and `SkillSlotState` stay exactly as they
  are — they were already internal; only the authoring/stat inputs change.

## Design Decisions

**Rate is the single exposed knob (chosen: "Generic Rate").** The stat is
`SkillStat.Rate`; the support is `IncreasedRateSupport` ("Increased Skill Speed").
Cooldown is a pure derivation of rate, never authored.

**Speed scaling is an *increased* modifier, not a multiplier (per user).** Both the
support and the player-level contribution feed the summed `increased` term
`(1 + Σincreased)` of the `Rate` fold. There is no Post multiplier for speed. This
is why the old `castSpeedMultiplier / recoverySpeedFactor` special case disappears
entirely: the rate flows through the same `StatModifierAccumulator.Resolve` fold as
every other stat, and the only rate-specific step is the final `1 / rate`
inversion.

**`baseRecoveryTime` → `baseRate` is a value transform, not just a rename.** A time
of `0.2s` is a rate of `5/s`. Every skill asset's stored number must be inverted
(`rate = 1 / time`), so `[FormerlySerializedAs]` is *not* usable — it would import
the old number under the new (inverted) meaning. Assets are migrated explicitly.

## Constraints & Invariants

- **SO templates are never mutated at runtime** (`skill-system.md` §Skills;
  `SkillSetCompiler.CompileDefinition` deep-copies before folding). The rework only
  reads `Skill.baseRate`; no new mutation. ✔
- **All stat scaling is compile-time, not per-frame** (`skill-system.md`
  §Compilation). `recoveryTime` is resolved once during `Compile`, stored on
  `RuntimeSkillDefinition.RecoveryTime`, and copied into `SkillSlotState` on
  equip/recompile via `PlayerSkillDriver` (line ~120). Unchanged. ✔
- **One fold per stat** (`StatModifierAccumulator.Resolve`:
  `(base*preMul + added) * (1+increased) * postMul`). Rate now uses this fold with a
  real base (`baseRate`) instead of the sentinel base `1f` used by the old
  `Resolve(RecoverySpeed, 1f)`. ✔
- **Division safety.** Increases are summed and could drive `(1+increased)` low or
  negative; `baseRate` is `Min(0.01)`. Guard: `recoveryTime = 1 / Max(epsilon, rate)`,
  and `SkillSlotState.SetRecoveryTime` already clamps to `Max(0.01, seconds)`. ✔
- **Aggregator is a stub** returning identity (`PlayerStatAggregator.Aggregate`).
  Identity for the rate contribution flips from `1.0` (multiplier) to `0.0`
  (increased percent); no live cast-speed data exists to migrate. ✔

## Mechanisms Reused vs Introduced

- **Reused:** the per-stat fold (`StatModifierAccumulator`), the `IIncreasedModifier`
  / `IncreasedSink` path, and `SnapshotModifiers.Contribute` (which already injects
  player snapshot terms into the accumulator). The player rate contribution is added
  there as `AddIncreased(Rate, ...)` alongside the existing Damage/Area terms.
- **Removed:** the special-case recovery formula
  `baseRecoveryTime * castSpeedMultiplier / max(0.01, recoverySpeedFactor)`.
- **Introduced:** nothing new — no new type, no new data path. The change is a
  rename + a value transform + moving one contribution from the multiplier bucket to
  the increased bucket.

## Minimal/Additive vs Refactor Comparison

- **Minimal/additive** — add `SkillStat.Rate` next to `RecoverySpeed`, keep
  `baseRecoveryTime`, and derive rate from it:
  - data flow: two representations of the same concept (time *and* rate) that must be
    kept in sync; the recovery formula stays.
  - new concepts/types: a redundant stat; a time↔rate translation step.
  - copies/translations added: one (time→rate) plus a lingering special case.
  - long-term cost: two sources of truth for "how fast a skill fires"; authoring
    confusion; the exact wart the user is trying to remove.
- **Refactor (chosen)** — replace time with rate end to end:
  - data flow: single source of truth (`baseRate` → folded `Rate` → `1/rate`).
  - concepts changed/removed: `RecoverySpeed`→`Rate`, `baseRecoveryTime`→`baseRate`,
    `CastSpeedMultiplier`→`IncreasedRatePercent`; the recovery formula is deleted.
  - copies/translations removed: the bespoke recovery formula and the
    multiplier-bucket cast-speed special case.
  - long-term benefit: rate folds identically to every other stat; cooldown is a
    derived internal; authoring surface matches player mental model.
- **Decision:** **refactor.** The additive path reintroduces the dual representation
  the rework exists to eliminate, and the aggregator stub means migration risk is
  limited to renamed serialized fields (handled in task 004).

## Default Decision Rule Applied

Time and rate describe the same domain concept ("how often a skill fires"). Per the
one-source-of-truth rule, we keep exactly one (rate) and delete the other, since the
only compatibility concern (serialized asset values) is a mechanical migration.

## Task List

- [001-stat-and-skill-surface.md](001-stat-and-skill-surface.md) — rename
  `SkillStat.RecoverySpeed`→`Rate`; replace `Skill.baseRecoveryTime`→`baseRate`.
- [002-increased-rate-support.md](002-increased-rate-support.md) — rename support to
  `IncreasedRateSupport`, author an increased-percent on `Rate`, preserve script GUID.
- [003-snapshot-and-compiler-fold.md](003-snapshot-and-compiler-fold.md) — snapshot
  `IncreasedRatePercent`; `SnapshotModifiers` adds increased-Rate; `ResolveRecoveryTime`
  becomes `1 / Resolve(Rate, baseRate)`.
- [004-asset-migration.md](004-asset-migration.md) — invert `baseRecoveryTime`→`baseRate`
  on 31 skill assets; migrate the support asset field/value; rename script `.cs`/`.meta`.
- [005-tests.md](005-tests.md) — update the three recovery tests + renamed type/fields.
- [006-docs.md](006-docs.md) — update `skill-system.md` recovery/cast-speed sections.

## Open Considerations

- **Damage/Area remain Post multipliers** on the snapshot (`DamageMultiplier`,
  `AreaSizeMultiplier`). The user's "increased not multiplier" instruction was scoped
  to speed/rate; Damage/Area are intentionally left unchanged. Flag for a later pass
  if the same principle should extend to them.
- **Support authored unit.** Task 002 switches the support's serialized input from a
  multiplier (`1.5`) to an increased percent (`0.5 = +50%`) to match the "scale by a
  percentage" framing. If you prefer to keep the multiplier-style input, keep the
  field and contribute `value - 1f` instead; note it changes the asset value in 004.
- **Task ordering.** 001→003 are code and interdependent (003 depends on the renamed
  stat/field and the new support). 004 (data) must land with the code or Unity drops
  the removed serialized fields to default. 005/006 follow.

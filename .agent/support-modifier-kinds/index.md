# Support Modifier Kinds — Make Additive Supports Impossible To Misuse

## Problem

`AdditiveSupport.Apply(SkillDefinition def)` hands every support **unconstrained
mutation** of the runtime definition copy. That single hole produces four distinct
symptoms:

1. **The name lies.** Supports named "additive" multiply the base value —
   `FasterProjectilesSupport` (`speed *= ...`,
   [FasterProjectilesSupport.cs:18-19](../../Assets/Scripts/Skills/Support/FasterProjectilesSupport.cs#L18-L19))
   and `ConcentratedEffectSupport` (`baseAreaSize *= ...`,
   [ConcentratedEffectSupport.cs:18](../../Assets/Scripts/Skills/Support/ConcentratedEffectSupport.cs#L18)).
2. **Order-dependent, wrong combination.** `ConcentratedEffect` (`×0.75`) +
   `IncreasedAoe` (`+50% of base`) produce different results depending on slot
   order. There is no "sum the increased, then apply the multipliers" guarantee.
3. **Fragile base reference.** `IncreasedAoeSupport` only works because the
   compiler happens to call the two-arg `Apply(def, baseDefinition)`
   ([SkillSetCompiler.cs:127](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L127)).
   The one-arg overload defaults `base = def`, silently breaking "% of base".
4. **Two parallel modifier systems.** Recovery speed bypasses `Apply` and routes
   through `SkillSupport.RecoverySpeedMultiplier`, summed in
   [ResolveRecoveryTime](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L152-L171).
   Player-level scaling (`DamageMultiplier`, `AreaSizeMultiplier`,
   `CastSpeedMultiplier`) is applied separately again in `BuildRuntime`. Three
   places scale the same stats with three different mechanisms.

## Goal

Model the four modifier kinds explicitly as **composable, typed kind interfaces**,
each exposing only the operation its kind permits, and have the compiler own the
math through **one per-stat fold** that both supports and the player snapshot feed.
After this change an "additive" support has no API by which it could multiply
base, and combination order stops mattering.

## The four modifier kinds (user-specified)

| Kind | Meaning | Fold term | Kind interface |
|---|---|---|---|
| 1. Base | modifies the base value (flat add) | `Σadded` | `IBaseValueModifier` |
| 2. Additive | adds a % of base on top (summed) | `(1 + Σincreased)` | `IIncreasedModifier` |
| 3. Multiplier | multiplies, pre-additive or post-increased | `Πpre` / `Πpost` | `IMultiplierModifier` |
| 4. Enable/disable | sets flags/behavior, not a numeric stat | (none — direct) | `IProjectileBehaviorModifier` / `IAoeBehaviorModifier` |

Kinds are **composable interfaces**, not mutually exclusive base classes — a support
implements one or more. All augment supports derive from `StatModifierSupport` (the
SO base carrying `SupportedSkillTags`); kind interfaces ride on top. This lets a
single support span kinds (e.g. **Piercing** = additive pierce-count +
behavior repeat-hit cooldown). Each interface still hands only its kind's
constrained sink, so an `IIncreasedModifier` cannot multiply or write base.

### Per-stat fold formula (authoritative)

```
effectiveBase = base × Πpre + Σadded
value         = effectiveBase × (1 + Σincreased) × Πpost
```

- `Πpre` (kind 3, Pre timing) multiplies the **intrinsic base only**, before flat
  adds and increases. "Multiply the base value before additive modifiers."
- `Σadded` (kind 1) flat additions.
- `(1 + Σincreased)` (kind 2) summed increased percents (`+50%` contributes `0.5`).
- `Πpost` (kind 3, Post timing) "more"-style final multipliers, including the
  player snapshot multipliers. "Multiply the final result after increased modifiers."

The Pre/Post distinction is meaningful only when flat adds exist (Pre scales
intrinsic base, Post scales the final). No current support uses Pre; the capability
is reserved and exercised by tests.

`PierceCount` is an integer stat: it folds like any other (only `added` is used
today) and `BuildRuntime` rounds the resolved value back to an int
(`Max(0, RoundToInt(...))`). Base pierce is the skill's authored `pierceCount`.

### Recovery is a speed→time stat

Recovery uses the same fold to produce a **recovery-speed factor**, then inverts:

```
recoverySpeedFactor = (1 × Πpre + Σadded) × (1 + Σincreased) × Πpost     // base speed = 1
recoveryTime        = baseRecoveryTime × castSpeedMultiplier / max(0.01, recoverySpeedFactor)
```

`IncreasedRecoverySpeedSupport` becomes an ordinary `AdditiveSupport` on the
`RecoverySpeed` stat. `castSpeedMultiplier` stays a recovery-**time** multiplier in
the resolver (preserves current numeric behavior; see
[CritEditModeTests.cs:112-149](../../Assets/Tests/EditMode/CritEditModeTests.cs#L112-L149)).

## Stat→kind mapping for existing supports

All derive from `StatModifierSupport` and implement the listed kind interface(s).

| Support | Kind interface(s) | Stat(s) / behavior | Serialized field (unchanged) |
|---|---|---|---|
| AddedDamage | `IBaseValueModifier` | `Damage` added | `addedDamage` |
| IncreasedAoe | `IIncreasedModifier` | `AreaSize` increased = `areaSizeMultiplier − 1` | `areaSizeMultiplier` |
| IncreasedRecoverySpeed | `IIncreasedModifier` | `RecoverySpeed` increased = `recoverySpeedMultiplier − 1` | `recoverySpeedMultiplier` |
| ConcentratedEffect | `IMultiplierModifier` | `AreaSize` Post `×areaSizeMultiplier` | `areaSizeMultiplier` |
| FasterProjectiles | `IMultiplierModifier` | `ProjectileSpeed`,`ProjectileLifetime` Post | `speedMultiplier`,`lifetimeMultiplier` |
| MultipleProjectiles | `IProjectileBehaviorModifier` | sets `count`,`spreadDegrees` | `count`,`spreadDegrees` |
| **Piercing** | `IBaseValueModifier` + `IProjectileBehaviorModifier` | `PierceCount` **added** (stacks/folds); `repeatHitCooldown` set | `pierceCount`,`repeatHitCooldown` |
| Homing | `IProjectileBehaviorModifier` | enables tracking + config | `trackingTurnSpeedDegrees`,`trackingQueryIntervalSeconds` |

Piercing is the canonical multi-kind support: pierce count is a numeric `BaseValue`
contribution (folds with base authored pierce and any future player +pierce), while
repeat-hit cooldown is a behavior set. `count` (MultipleProjectiles) stays an
override per decision 3 — only pierce was promoted to additive. The asymmetry is
intentional; `count` can be promoted later the same way if wanted.

Snapshot mapping: `DamageMultiplier`→`Damage` Post, `AreaSizeMultiplier`→`AreaSize`
Post, `CastSpeedMultiplier`→recovery-time multiplier in the resolver. These were
already applied as final multipliers, so Post preserves results exactly.

## Constraints & invariants the change must respect

- **No asset migration.** `.asset` files reference each concrete support by script
  GUID and serialize fields by name (e.g.
  [IncreasedAoeSupport.asset:15](../../Assets/ScriptableObjects/Supports/IncreasedAoeSupport.asset#L15)
  `areaSizeMultiplier: 1.8`). Keep every concrete support's file path, class name,
  namespace, and serialized field names. Re-parenting a class does not change its
  GUID or field serialization. *(source: the eight `.asset` files under
  `Assets/ScriptableObjects/Supports/`)*
- **Compile is not per-frame.** Compilation runs on equip/loadout change only
  ([skill-system.md Compilation](../../Docs/reference/game-logic/skill-system.md)),
  so per-compile allocation is acceptable — but the fold uses fixed-size storage
  keyed by stat enum, so it allocates nothing hot.
- **SO templates are never mutated.** All work happens on the `DeepCopy()`
  ([skill-system.md Runtime Modification]) and the runtime instance.
- **Set isolation.** Supports only modify the one skill in their set; this change
  does not touch trigger/chain compilation.
- **Behavior parity.** Existing EditMode tests pin the numeric contract (area
  `2.5`/`26`, recovery `0.2`/`0.3`, added damage `15`) and must pass unchanged
  except for net-new tests. *(source:
  [CritEditModeTests.cs](../../Assets/Tests/EditMode/CritEditModeTests.cs),
  [SkillValidationEditModeTests.cs](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs))*
- **Validator coverage.** Tag validation currently keys on `AdditiveSupport`
  ([SkillLoadoutValidator.cs:73](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L73))
  and must continue to cover every numeric/behavior support after re-parenting.

## Mechanisms reused vs. introduced

- **Reused:** the existing additive-sum-then-divide model already proven for
  recovery speed (`recoverySpeedMultiplier += value − 1`,
  [SkillSetCompiler.cs:165](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L165))
  is generalized into the per-stat fold — this is the canonical model, not a new
  one. The `DeepCopy` → mutate-copy → `BuildRuntime` → conversion pipeline stays.
- **Introduced:** `SkillStat` enum (incl. `PierceCount`), `StatModifierAccumulator`
  (the fold), typed contribution **sink** structs (one per kind, each able to write
  only its kind), shape-typed **behavior context** structs (expose only behavior
  fields, never numeric base fields), the `StatModifierSupport` SO base, and the
  **composable kind interfaces** (`IBaseValueModifier`, `IIncreasedModifier`,
  `IMultiplierModifier`, `IProjectileBehaviorModifier`, `IAoeBehaviorModifier`). The
  new types exist to *remove* the unconstrained `Apply` hole, not to wrap it.

## Design validation against the invariants

- *Impossible to misuse:* a concrete support only ever receives a kind-specific
  sink (`AddIncreased(stat, pct)` etc.) or a behavior context exposing only
  count/spread/cooldown/tracking. There is no path from an `IIncreasedModifier` to a
  multiply or to a base-value write, even on a multi-kind support — each interface
  hands a different, narrowly-scoped sink. ✔
- *Composability:* a support spans kinds by implementing multiple interfaces
  (Piercing = `IBaseValueModifier` + `IProjectileBehaviorModifier`) without widening
  any single sink's authority. ✔
- *Order independence:* increases sum, multipliers take a product; both are
  commutative within their term, so slot order no longer changes numeric output. ✔
- *Single source of truth:* one `StatModifierAccumulator` per stat consumes both
  support and snapshot contributions; `BuildRuntime` and `ResolveRecoveryTime`
  read only the folded result. The three parallel scaling paths collapse to one. ✔
- *Parity:* with existing assets (no Pre multipliers, increases as `x−1`, snapshot
  as Post) the formula reproduces every pinned test value (worked in
  [005-tests.md](005-tests.md)). ✔
- *No asset churn:* re-parenting + identical field names → assets and reflection
  tests untouched. ✔

## Minimal/additive vs. refactor comparison

- **Minimal/additive approach** (keep `Apply`, just fix `IncreasedAoe`):
  - resulting data flow: still per-support free mutation; recovery + snapshot still
    separate paths.
  - new concepts/types: none.
  - copies/translations added: none.
  - long-term cost: the misuse hole stays open; every future support can still
    multiply base from an "additive" slot; combination order stays undefined; three
    scaling paths persist. Does not satisfy "impossible to misuse."
- **Refactor approach** (this plan — typed kinds + unified fold):
  - resulting data flow: supports + snapshot → one per-stat accumulator → fold →
    runtime; behavior via constrained contexts.
  - existing concepts/types changed: `AdditiveSupport` removed in favor of kind
    interfaces; concrete supports re-based onto `StatModifierSupport` + interfaces;
    `SkillSupport.RecoverySpeedMultiplier` removed; pierce promoted to a folded stat;
    compiler fold; validator base-type swap.
  - copies/translations removed: the bespoke recovery path and the separate
    snapshot multiply collapse into the fold.
  - long-term benefit: kind is type-enforced; math is centralized and
    order-independent; one place to add a stat or a player modifier source.
- **Decision: choose refactor.** Reason: the user's requirement ("impossible to
  misuse") is unachievable additively — it is precisely the unconstrained `Apply`
  surface that must be removed. The refactor also collapses three duplicate scaling
  paths into one source of truth, satisfying the default decision rule below.

## Default decision rule applied

Two representations described the same concept ("scale stat X"): per-support
`Apply` mutation and snapshot multipliers in `BuildRuntime`. Per the rule, we
refactor toward one source of truth — the per-stat fold — since there is no
compatibility reason to keep them separate (the snapshot aggregator is still a stub
returning identity, [PlayerStatAggregator](../../Assets/Scripts/Skills/PlayerStatSnapshot.cs#L28-L33)).

## Tasks

| # | File | Summary | Depends on |
|---|---|---|---|
| 001 | [001-core-modifier-model.md](001-core-modifier-model.md) | `SkillStat`, `StatModifierAccumulator`, kind sinks, behavior contexts | — |
| 002 | [002-typed-support-bases.md](002-typed-support-bases.md) | Typed base classes; remove `RecoverySpeedMultiplier` | 001 |
| 003 | [003-concrete-supports.md](003-concrete-supports.md) | Re-implement the 8 concrete supports onto new bases | 002 |
| 004 | [004-compiler-and-validator.md](004-compiler-and-validator.md) | Compiler fold + behavior application + recovery; validator base swap | 001-003 |
| 005 | [005-tests.md](005-tests.md) | Keep pinned tests; add determinism/pre-post/recovery tests | 004 |
| 006 | [006-docs.md](006-docs.md) | Update `skill-system.md` to the four-kind model | 004 |

## Decision updates (post-review)

- **Decision 2 refined → composable interfaces.** Original choice was "typed base
  class per kind"; Piercing spans two kinds, which single inheritance cannot express.
  Kinds are now typed **interfaces** with the same per-kind enforcement, plus
  composability. `StatModifierSupport` remains the SO base for `SupportedSkillTags`.
- **Decision 3 amended → pierce is additive.** Pierce count moves from override to a
  `BaseValue` (added) contribution on `SkillStat.PierceCount`, so it stacks across
  supports and folds with future player +pierce. `repeatHitCooldown` stays behavior
  (set). `count` (MultipleProjectiles) still overrides — only pierce changed.

## Open questions / considerations

- **Field naming polish (deferred).** `areaSizeMultiplier = 1.8` meaning "+80%
  increased" is itself misleading, but renaming to a percent needs asset value
  migration (FormerlySerializedAs preserves the number, not the semantics). Kept
  out of scope to honor "no asset migration"; the typed base already prevents the
  multiply-base misuse regardless of field name. Revisit if a designer-facing
  rename is wanted.
- **AOE behavior context** is defined minimally (Count, DirectDamageEnabled) for
  symmetry; no AOE behavior support exists yet.
- **Doc table drift:** `skill-system.md` lists Concentrated Effect as modifying
  damage too, but the code only scales area. 006 aligns the doc to the code.

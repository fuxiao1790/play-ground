# 006 — Docs

Update [skill-system.md](../../Docs/reference/game-logic/skill-system.md) to the
four-kind model. Depends on 004 (describe shipped behavior).

## Edits

1. **Supports section** (`abstract class SkillSupport` / `AdditiveSupport`
   snippets, ~lines 300–324): replace the single `AdditiveSupport.Apply` description
   with the kind model:
   - `StatModifierSupport` SO base (carries `SupportedSkillTags`) plus **composable
     kind interfaces**: `IBaseValueModifier` (flat added), `IIncreasedModifier`
     (increased %), `IMultiplierModifier` (Pre/Post more),
     `IProjectileBehaviorModifier` / `IAoeBehaviorModifier` (enable/set).
     `ConversionSupport` unchanged.
   - State that a support may implement several interfaces (Piercing = base
     pierce-count + behavior cooldown) and that each interface hands only its kind's
     constrained sink.
   - State the fold formula:
     `value = (base × Πpre + Σadded) × (1 + Σincreased) × Πpost`, increases sum,
     multipliers product, order-independent.
   - Note recovery is a speed→time stat folded the same way then inverted, and that
     the player snapshot multipliers feed the **same** per-stat fold as Post
     multipliers (one source of truth; remove the implication of a separate path).

2. **Support tables** (~lines 326–350): set each support's kind interface(s) and the
   stat/behavior it contributes (mirror the index mapping table). Mark Piercing as
   multi-kind with **additive** pierce count. Fix the Concentrated Effect row to
   area-only (code does not scale damage).

3. **Compilation pseudocode** (~lines 612–621): replace the
   `for each support: if AdditiveSupport: support.Apply(def)` block and the
   `summedSupportRecoverySpeed` recovery line with the accumulator build (per-kind
   interface dispatch over supports + snapshot — note multi-kind supports hit more
   than one branch), behavior-context application, fold-based `BuildRuntime`, and
   `recoveryTime = baseRecoveryTime × castSpeed / recoverySpeedFactor`.

4. **Layer 1.5 baking** (~lines 102–110): note the snapshot multipliers are Post
   multipliers in the unified fold, not an independent multiply.

## Acceptance criteria
- Doc describes the four kinds, the formula, order-independence, and the single fold.
- No stale reference to `Apply(def)` or `RecoverySpeedMultiplier`.

## Scope: small. Documentation only.

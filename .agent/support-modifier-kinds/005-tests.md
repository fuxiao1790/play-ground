# 005 — Tests

Confirm parity on the pinned numeric contract and add coverage for the new
guarantees. Depends on 004.

## Parity (must pass unchanged)
These reflection-driven tests set serialized fields by name; the names survive, so
they should pass without edits. Re-confirm:
- `Compiler_AppliesAreaSizeMultiplier_IntoRuntimeAoe` → `2.5`
  (base `1.25` × Post `2`).
- `Compiler_StacksIncreasedAoeSupport_AdditivelyFromBaseAreaSize` → `26`
  (base `10`, two increased `0.8` → `×2.6`).
- `Compiler_UsesSkillBaseRecoveryTime` → `0.2`
  (base `0.4` × castSpeed `0.5` / speedFactor `1`).
- `Compiler_StacksIncreasedRecoverySpeedSupport_Additively` → `0.3`
  (base `0.6` / speedFactor `2.0`).
- `CompilerAppliesAdditiveSupportsToStackingSupportDetonation` → `15`
  (added damage `5` on base `10`, folded before conversion).
- `ValidatorWarnsWhenProjectileSupportIsOnAoeSkill` → one
  `UnsupportedSupportForSkill` (validator now keys on `StatModifierSupport`).

## New tests (`CritEditModeTests` or a new `ModifierFoldEditModeTests`)
1. **Increased + multiplier are order-independent.** Build a set with
   `IncreasedAoe(1.5)` and `ConcentratedEffect(0.5)` on an AOE; assert AreaSize is
   identical regardless of support order, equal to `base × (1+0.5) × 0.5`.
2. **Multipliers take a product (not a sum).** Two AOE multiplier supports `×1.5`
   and `×2.0` → `base × 3.0` (not `×2.5`).
3. **Added + increased + multiplier compose per formula.** On damage: base `10`,
   added `5`, increased `1.0`, Post `×2` → `(10+5)×2.0×2 = 60`.
4. **Pre vs Post differ only with flat adds.** A test-only `MultiplierSupport`
   emitting a Pre `×2` on damage with added `+10` on base `5`:
   `(5×2 + 10)×1×1 = 20`; same magnitudes as Post give `(5+10)×1×2 = 30`.
   (Use a private test support subclass to emit Pre, since no shipping support uses
   it.)
5. **Recovery folds through the accumulator** alongside `castSpeedMultiplier`:
   base `0.5`, one increased recovery `+1.0` (speedFactor `2`), castSpeed `0.5`
   → `0.5×0.5/2 = 0.125`.
6. **Pierce is additive across supports.** Two `PiercingSupport` with `pierceCount`
   `2` and `3` on a projectile with base authored pierce `1` → runtime `PierceCount`
   `= 1 + 2 + 3 = 6` (was last-wins `3` before). Confirms the multi-kind support is
   collected on its `IBaseValueModifier` branch.
7. **Piercing still sets repeat-hit cooldown** (its behavior branch): assert
   `RepeatHitCooldown` equals the support's value, proving the same support is visited
   as both base-value and behavior.
8. **Behavior support cannot reach numeric base** — compile-time guarantee, assert
   structurally via a small reflection check that `ProjectileBehaviorContext` has no
   `damage`/`speed`/`lifetime`/`pierce` setter (documents intent; optional).

## Acceptance criteria
- Full EditMode suite green.
- New tests fail if the fold reverts to order-dependent or sum-of-multipliers math.

## Scope: small–medium.

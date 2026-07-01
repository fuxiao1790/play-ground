# 006 — Docs

## Goal
Bring `Docs/reference/game-logic/skill-system.md` in line with the rate model.

## Edits (by section)

- **Concepts → Skill** (~L9): "base recovery time" → "base rate (attacks/casts per
  second)".
- **Layer 1 → Equipment State** (~L84): "base recovery time" → "base rate".
- **Layer 1.5 → `PlayerStatSnapshot` fields** (~L106–110): replace
  `castSpeedMultiplier` with `increasedRatePercent` (increased percent on the rate
  stat; `0` = identity).
- **Layer 1.5 → Baking** (~L112–116): drop "castSpeedMultiplier is applied as a
  recovery-time multiplier"; state that `increasedRatePercent` contributes to the
  `Rate` increased bucket, and `recoveryTime = 1 / rate`.
- **Layer 2 → `SkillSlotState`** (~L134–139): keep `recoveryTime`, but note it is a
  derived internal (`1 / rate`), not authored.
- **Skills → `Skill` snippet** (~L185–189): `float baseRecoveryTime` →
  `float baseRate` with a "casts/sec; folded through the Rate stat" comment.
- **Supports → fold** (~L359–376): replace the "Recovery is folded as recovery speed
  and then inverted" block. New text: the `Rate` stat uses the same fold as other
  stats; both `IncreasedRateSupport` and the player `increasedRatePercent` feed the
  summed increased term; `recoveryTime = 1 / max(0.01, rate)`. Remove the
  `recoverySpeedFactor` / `castSpeedMultiplier` formula.
- **Supports → augment table** (~L389): "Increased Recovery Speed | IIncreasedModifier
  | Increased percent on RecoverySpeed" → "Increased Skill Speed | IIncreasedModifier
  | Increased percent on Rate".
- **Supports → compatible-tags table** (~L402): rename "Increased Recovery Speed" →
  "Increased Skill Speed".
- **Compilation pseudocode** (~L685–686): replace
  `recoverySpeedFactor = acc.Resolve(RecoverySpeed, 1)` /
  `runtime.RecoveryTime = BaseRecoveryTime * CastSpeedMultiplier / max(...)` with
  `rate = acc.Resolve(Rate, skill.BaseRate)` and
  `runtime.RecoveryTime = 1 / max(0.01, rate)`.
- **Authoring → Creating a Skill** (~L852): step 3 `baseRecoveryTime (cooldown in
  seconds)` → `baseRate (attacks/casts per second)`.

## Note
`skill-system.md` is a design reference ("current intent, not final decisions").
Keep edits scoped to the recovery/rate vocabulary; do not restructure unrelated
sections.

## Acceptance
- No occurrence of `RecoverySpeed`, `baseRecoveryTime`, or `castSpeedMultiplier`
  remains in the doc; rate vocabulary and the `1 / rate` derivation are described.

## Dependencies
- Reflects 001–003. Land after the code is settled.

## Scope
Small–medium (documentation only).

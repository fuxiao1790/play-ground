# 004 - Replace Legacy Tests And Prove Edge Isolation

## Goal

Test new vocabulary and behavior directly, including independent multipliers
and same-asset adjacency isolation.

## Changes

### EditMode

Update SkillValidationEditModeTests and related compiler/registration tests:

1. Verify TriggerEnergy compilation for every skill kind.
2. Verify RuntimeHitEnergyTrigger composition:
   - TriggeredSkill is adjacent target runtime instance
   - both multipliers and RetentionSeconds copy independently
   - object is not RuntimeSkillDefinition
3. Verify payload formula:
   - source TriggerEnergy `2`
   - contribution multiplier `0.5`
   - target TriggerEnergy `4`
   - requirement multiplier `1.5`
   - result EnergyPerHit `1`, EnergyRequired `6`
4. Verify registration assigns stable distinct AccumulatorId values.
5. Add same-asset graph:

   ```text
   S(asset A) -> T1 -> S(asset A) -> T2 -> S(asset A)
   ```

   Assert three runtime skill instances, two runtime trigger instances, distinct
   ids, root payload references only T1, and middle template carries only T2.
6. Prove incoming-trigger nodes remain excluded from direct roots.
7. Prove interval trigger behavior unchanged.

### PlayMode

Update AoeSimulationTests, AoePlayModeTests, TargetedSkillPlayModeTests, and
projectile integration tests found during implementation:

1. Replace legacy fixtures/helpers with HitEnergyPayload and TargetHitEnergy.
2. Fractional case: `0.4 + 0.4 + 0.4` against `1.0` emits once, retains `0.2`.
3. Large deposit emits once per complete requirement and retains capped
   overflow.
4. Preserve retention expiry, projectile activation, impact/lingering AOE,
   targeted source, buffer-full refresh/drop, and unknown-kind cases.
5. Prove registered template values control output; energy affects only timing
   and activation count.
6. End-to-end adjacency: S1 deposits only into T1 accumulator; spawned S2 alone
   can deposit into T2. Repeat with same Skill asset/value in all three slots.
7. Update managed assertions to HitEnergyProgress names/float tolerances.

## Acceptance Criteria

- Tests use only new domain symbols.
- Tests fail if ids deduplicate by asset, type, template, or equal energy.
- Tests fail if S1 directly funds T2/S3.
- Projectile, AOE, and targeted sources plus projectile/AOE outputs covered.
- Next-update timing and floating-point tolerances explicit.

## User-Run Verification

Agent must not run Unity tests. Ask user to run:

- **EditMode:** `PlayGround.Tests.EditMode.SkillValidationEditModeTests` plus
  touched recursive compiler/registration classes.
- **PlayMode:** touched hit-energy methods in AoeSimulationTests,
  AoePlayModeTests, TargetedSkillPlayModeTests, and projectile integration
  class.

Require and review:

- `Logs/TestResults-EditMode-HitEnergyTrigger.xml`
- `Logs/TestResults-PlayMode-HitEnergyTrigger.xml`

Do not report tests passed before reviewing XML.

## Dependencies

- Tasks 001-003.

## Scope / Complexity

High.


# 003 - Build ECS Hit Energy Accumulator And Activation Flow

## Goal

Give ECS path fresh names matching stored energy and activation behavior.

## Changes

1. Replace `TargetStackEntry` with `TargetHitEnergy` in target proxy archetype:
   - `AccumulatorId`
   - `StoredEnergy`
   - `EnergyRequired`
   - `ExpiresAt`
   - `HitEnergySpawn`
   - retain internal capacity and 32-entry bound
2. `CombatApplyFinalizeSingleSystem`:
   - Rename stack lookup/flags/helpers around HitEnergy.
   - Locate/create by AccumulatorId.
   - Add HitEnergyPayload.EnergyPerHit once per accepted hit.
   - Refresh EnergyRequired, ExpiresAt, and HitEnergySpawn from fire-time
     payload.
   - Do not scale energy with damage scale, crit, count, area, chain falloff,
     or delta time.
3. Replace `StatusProcessSystem` with `HitEnergyActivationSystem`:
   - rename query/job/limits/helpers around activation vocabulary
   - expire when current time reaches ExpiresAt
   - complete activations = floor(StoredEnergy / EnergyRequired)
   - emit at most remaining per-target budget up to 256
   - subtract only emitted activation cost
   - retain fractional remainder and capped overflow
   - clamp floating-point subtraction residue safely
   - preserve spawn-id uniqueness and existing event queues
4. Replace `StatusStackSnapshot` with `HitEnergyProgress`:
   - `AccumulatorId`
   - `StoredEnergy`
   - `EnergyRequired`
   - `RetentionRemaining`
5. Update ICombatTarget, CombatApplyBridge, PlayerRoot, and MobRoot collection
   and method names consistently. Presentation remains non-authoritative.
6. Leave unrelated CombatTickResult.HitCount unchanged; it counts accepted
   combat hits, not hit-energy progress.

## Acceptance Criteria

- One accepted source hit adds exactly payload EnergyPerHit.
- One activation consumes exactly EnergyRequired and queues one HitEnergySpawn.
- Fractional remainder persists.
- Current-update deposit becomes eligible next update.
- Retention and zero-retention disable behavior remain.
- 32-entry and 256-activation bounds remain.
- No old stack/debuff/detonation/status symbol remains for this feature.
- CombatTickResult.HitCount semantics remain unchanged.

## Dependencies

- Task 002.

## Scope / Complexity

High. ECS component/buffer rename, Burst job math, system rename, and managed
progress contract change.

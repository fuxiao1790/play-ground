# 005 - Document Eligibility And Tick Result Contracts

## Goal

Make faction policy, accepted-hit semantics, and tick duration authoritative in
project documentation.

## Changes

1. Update `Docs/contracts/target-proxy.md`:
   - describe expanded `TargetFaction` fields and both filter modes;
   - replace unconditional same-faction inequality guarantee with shared
     eligibility rule;
   - state policy is stamped at proxy creation and immutable in current scope;
   - state selected policy chooses faction, not individual source actor.

2. Update `Docs/contracts/combat-hit-and-tick-results.md`:
   - define `HitCount` as accepted hit event count, including non-damaging hits;
   - distinguish direct-damage-only `DamageTaken` and `CritCount`;
   - document `TickDeltaSeconds` as source ECS finalizer delta;
   - state results/callbacks exist only for targets with hit/status results, not
     every simulation tick.

3. Update flow/layer docs:
   - `Docs/flows/collision-to-combat-result.md` for policy qualification and tick
     result fields;
   - `Docs/layers/ecs-simulation.md` for target policy ownership and timing;
   - `Docs/layers/presentation-and-feedback.md` for unchanged aggregate callback
     and tick metadata delivery;
   - `Docs/flows/runtime-frame.md` if wording assumes unconditional hostile-only
     selection.

4. Update directly conflicting reference docs found by search, especially:
   - `Docs/reference/simulation/projectile-system.md`;
   - `Docs/reference/simulation/targeted-system.md`;
   - `Docs/reference/simulation/project-ecs-implementation.md`;
   - `Docs/reference/simulation/spawn-template-registry.md`;
   - `Docs/contracts/spawn-events-and-commands.md` where "nearest hostile" now
     means nearest target eligible under target policy.

5. Keep scope statement explicit: no summon actor, movement, energy gauge,
   attached-skill trigger, prefab, or authoring implementation is delivered.

## Acceptance Criteria

- No current doc claims collision always uses only faction inequality.
- `TargetFaction` policy has one documented source of truth.
- Acquisition and collision docs describe identical eligibility semantics.
- Hit count and tick delta semantics match implementation and tests.
- Docs preserve aggregated-result/no-managed-per-hit invariant.
- Out-of-scope GameObject behavior is not described as implemented.

## Dependencies

- Tasks 001-003 finalized so names and semantics are stable.

## Estimated Scope / Complexity

Medium. Several authoritative and reference docs contain old hostile-only or
result-shape wording.


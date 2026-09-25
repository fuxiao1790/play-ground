# Collision To Combat Result

## Purpose

Trace how projectile, AOE, and targeted hits become ECS-owned combat state and
compact presentation results.

## Sequence

1. Projectile/AOE collision systems query unmanaged target proxy data, as does
   `TargetedResolveSystem` — a chain selects victims by broadphase query instead
   of a simulated collider, then joins this path unchanged.
2. Collision systems qualify hits with broad phase, narrow phase, target-policy
   eligibility, and repeat-hit gates.
3. Accepted hits emit plain data hit events and optional spawn/VFX consequences.
4. Before hit finalization, `HitEnergyActivationSystem` processes
   `TargetHitEnergy` accrued by prior updates and may enqueue registered output
   spawns.
5. `CombatApplyFinalizeSingleSystem` groups hits by target proxy, rolls crits,
   sums damage, updates `Health`, deposits each accepted hit's float
   `EnergyPerHit` into `TargetHitEnergy`, refreshes expiry, and freezes
   `CombatTickResult`. Newly deposited energy becomes eligible on the next
   simulation update.
6. Presentation bridge resolves `TargetCompanion` and calls managed target
   feedback once per changed target.

## Producers

`ProjectileDiscreteCollisionSystem`, `ProjectileContinuousCollisionSystem`, AOE
collision systems, `TargetedResolveSystem`, and `HitEnergyActivationSystem`.

## Consumers

`CombatApplyFinalizeSingleSystem`, `HitEnergyActivationSystem`, presentation bridge,
actor roots, and spawn expansion systems for follow-up events.

## Contracts Used

- [Target Proxy](../contracts/target-proxy.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [VFX Requests](../contracts/vfx-requests.md)

## Layer Boundaries Crossed

- [ECS Simulation](../layers/ecs-simulation.md) to
  [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

Hit-energy activation runs before current-update hit finalization. Finalization
runs after projectile/AOE collision and targeted resolve, and before spawn
expansion. A requirement reached during finalization activates on the next
simulation update. Activation spends complete requirements, preserves float
remainder and capped overflow, and emits through `HitEnergySpawn`; registered
template values remain output authority.
Managed target callbacks run after finalized result data exists.

## Failure / Edge Cases

Dense hit frames produce aggregated target results, not one managed plain-damage
callback per hit. Per-hit authored side effects must stay in ECS consequence
paths or dedicated semantic events.

## Related Decisions

- [ADR-004](../decisions/adr-004-target-proxy-collision.md)
- [ADR-006](../decisions/adr-006-ecs-aggregated-combat-results.md)

## Notes / TODOs

Older reference docs still mention `DamageReplayEvent` and
`DamageDispatchBridge` in places. TODO: verify every old name against current
`CombatApplyFinalizeSingleSystem`/`CombatApplyBridge` code before deleting legacy
wording.

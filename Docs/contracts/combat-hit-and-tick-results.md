# Combat Hit And Tick Results

## Purpose

Define data that carries collision outcomes into ECS health/status application
and compact presentation feedback.

## Produced By

Hit data is produced by [ECS Simulation](../layers/ecs-simulation.md)
collision systems. Compact results are produced by combat apply/finalize
systems.

## Consumed By

[ECS Simulation](../layers/ecs-simulation.md), status processing, spawn
expansion, and [Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Current key data:

- `CombatHitEvent`: `{ Source, Target, DamageScale }`, where `Source` is the
  projectile/AOE/targeted entity that produced the hit and `Target` is the
  target proxy entity. An unset scale (`0`) means `1`; targeted links use their
  falloff scale explicitly.
- `CombatHitPayload`: source-side ECS component carrying damage amount, crit
  chance, crit multiplier, direct-damage flag, source node id, and
  `HitEnergyPayload`.
- `Health`
- `TargetHitEnergy`
- `CombatTickResult`
- `HitCount` — accepted `CombatHitEvent` count for the target in one finalizer
  update, including non-damaging/status-only hits
- `DamageTaken` / `CritCount` — direct-damage-only aggregates, accrued only
  when the accepted hit's `CombatHitPayload.DirectDamageEnabled` is true
- `TickDeltaSeconds` — the finalizer's `SystemAPI.Time.DeltaTime` for the
  update that produced this result
- `Health` — final health snapshot
- `StatusStart` / `StatusCount` — changed status range

Older reference docs mention `DamageReplayEvent` and `DamageDispatchBridge`.
TODO: verify remaining legacy names against current code before removing them.

## Guarantees

Plain damage and status can be aggregated by target in ECS. Managed presentation
receives compact target results, not one plain-damage callback per raw hit.

Presentation callbacks fire only for targets whose result has hit or status
content (`HitCount > 0 || StatusCount > 0`), not for every simulation tick or
every target. Because `HitCount` now counts every accepted hit rather than
only direct-damage hits, a non-damaging/status-only accepted contact also
reaches the callback — previously such a contact could produce `HitCount == 0`
and be skipped unless it also changed status.

Every hit source archetype that can enqueue `CombatHitEvent` carries
`CombatHitPayload`, so finalize can resolve payload data through one read-only
`ComponentLookup<CombatHitPayload>` indexed by `CombatHitEvent.Source`.

Finalize multiplies the payload damage by `DamageScale` before it rolls crit and
applies the crit multiplier. This preserves falloff on both normal and critical
hits.

## Restrictions

Do not mix spawn-routing-only data into damage result contracts. Do not make VFX
or presentation code authoritative for damage/status decisions.

## Lifetime

Raw hit events live until finalize consumes them. `CombatTickResult` lives until
presentation bridge dispatches and clears result data.

## Ordering

`HitEnergyActivationSystem` processes prior-update target energy before current
hit finalization and spawn expansion. Finalization deposits current-update
energy, so it cannot activate until the next update. Presentation bridge runs
after finalize.

## Related Layers

- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Flows

- [Collision To Combat Result](../flows/collision-to-combat-result.md)
- [Runtime Frame](../flows/runtime-frame.md)
- [Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md)

## Notes / TODOs

TODO: reconcile old proposed combat-state redesign wording with current
`CombatApplyFinalizeSingleSystem` implementation notes.

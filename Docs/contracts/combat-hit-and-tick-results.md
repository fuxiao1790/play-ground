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

- `CombatHitEvent`
- `CombatHitPayload`
- `TargetHealth`
- `TargetStackEntry`
- `CombatTickResult`
- source node/type/id metadata
- damage amount, crit chance, crit multiplier, direct-damage flag
- hit count, crit count, aggregate damage, final health, and changed status
  ranges

Older reference docs mention `DamageReplayEvent` and `DamageDispatchBridge`.
TODO: verify remaining legacy names against current code before removing them.

## Guarantees

Plain damage and status can be aggregated by target in ECS. Managed presentation
receives compact target results, not one plain-damage callback per raw hit.

## Restrictions

Do not mix spawn-routing-only data into damage result contracts. Do not make VFX
or presentation code authoritative for damage/status decisions.

## Lifetime

Raw hit events live until finalize consumes them. `CombatTickResult` lives until
presentation bridge dispatches and clears result data.

## Ordering

Hit finalization runs after collision and before status detonation spawn
expansion. Presentation bridge runs after finalize.

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

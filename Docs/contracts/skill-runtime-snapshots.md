# Skill Runtime Snapshots

## Purpose

Define plain data copied from authored skill/game-logic state before combat
simulation owns it.

## Produced By

[Game Logic](../layers/game-logic.md), especially skill compilation and spawn
translation.

## Consumed By

[Combat Bridge](../layers/combat-bridge.md) and
[ECS Simulation](../layers/ecs-simulation.md).

## Fields / Shape

Current snapshot categories:

- runtime projectile definitions (includes copied trigger-authored launch-aim
  mode/range for a trigger's projectile target only; root definitions keep the
  disabled default — see
  [Skill Gameplay System](../reference/game-logic/skill-gameplay-system.md#projectile-launch-aim))
- runtime AOE definitions
- `CombatHitPayload`
- `StackEffectSnapshot`
- `TimedSpawnComponent`
- spawn template keys (`Hash128`) — used by all follow-up slots

## Guarantees

Snapshots contain values needed by in-flight combat entities. They are safe for
Burst-compatible ECS paths when copied into event/component data.

## Restrictions

Snapshots must not contain managed references, strings, GameObjects,
Transforms, Colliders, ScriptableObjects, or GC handles.

## Lifetime

Snapshots are created before spawn and copied through requests, events,
commands, and ECS components. In-flight entities keep their own copied values.

## Ordering

Resolve authored data and register timed templates before commands initialize
runtime entities.

## Related Layers

- [Game Logic](../layers/game-logic.md)
- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)

## Related Flows

- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

Detailed reference:
[spawn-template-registry.md](../reference/simulation/spawn-template-registry.md).

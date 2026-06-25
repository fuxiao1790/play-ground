# Spawn Requests

## Purpose

Define managed DTOs that carry game-logic spawn intent into `CombatRoot`.

## Produced By

[Game Logic](../layers/game-logic.md) and scene actor roots.

## Consumed By

[Combat Bridge](../layers/combat-bridge.md).

## Fields / Shape

Current request types:

- `ProjectileSpawnRequest`
- `AoeSpawnRequest`
- `ProjectileAoeSpawnRequest`

They carry resolved runtime values such as position, direction, faction, type
ids, geometry, damage/crit values, lifetime, tracking, count/spread, render
data, VFX ids, stack snapshots, and optional child/impact spawn snapshots.

## Guarantees

Requests are managed-side boundary objects. They should contain values that can
be validated and copied into plain ECS event data.

## Restrictions

Do not let ECS systems read request objects directly. Do not include live
GameObjects, Transforms, Colliders, or ScriptableObjects as in-flight simulation
dependencies.

## Lifetime

Requests are short-lived. After `CombatRoot` converts them into spawn events,
simulation must depend on the event snapshot, not the request object.

## Ordering

Requests must be produced after game logic resolves authoring data and before
the spawn event is appended to the scope buffer.

## Related Layers

- [Game Logic](../layers/game-logic.md)
- [Combat Bridge](../layers/combat-bridge.md)

## Related Flows

- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Mob Spawn And Behaviour](../flows/mob-spawn-and-behaviour.md)

## Notes / TODOs

TODO: verify whether `ProjectileAoeSpawnRequest` should remain a separate
public request contract or collapse into `AoeSpawnRequest` docs.

# Combat Root API

## Purpose

Define the managed bridge API used by scene/game logic to register combat
resources, register targets, and submit combat spawn intent.

## Produced By

[Combat Bridge](../layers/combat-bridge.md), mainly `CombatRoot`.

## Consumed By

[Scene And Authoring](../layers/scene-and-authoring.md) and
[Game Logic](../layers/game-logic.md).

## Fields / Shape

Key API surface:

- projectile template/type registration
- AOE config/type registration
- timed spawn template registration returning `Hash128`
- registered projectile/AOE template spawning
- projectile/AOE render id and render-template lookup
- AOE VFX-id assignment
- target registry access for actor registration
- `Spawn(ProjectileSpawnRequest, CombatFaction)`
- `Spawn(AoeSpawnRequest, CombatFaction)`
- `Spawn(ProjectileAoeSpawnRequest, CombatFaction)`
- scope/world binding (one root, faction is per-spawn not per-root)

## Guarantees

Valid submissions are converted into spawn events on the shared scope buffer.
Resource registration assigns runtime ids and render/VFX catalog data before
simulation uses those ids.

## Restrictions

The API must not allocate projectile/AOE entities directly. It must not run
high-count collision logic or call managed target damage callbacks from raw
simulation events.

## Lifetime

`CombatRoot` acquires ECS world/scope ownership on bind/enable and releases it
on teardown. One root serves all factions; resources are cleaned when the root
is destroyed.

## Ordering

Register resources before spawn submissions. Register targets before they are
expected to receive projectile/AOE hits.

## Related Layers

- [Combat Bridge](../layers/combat-bridge.md)
- [Scene And Authoring](../layers/scene-and-authoring.md)
- [Game Logic](../layers/game-logic.md)

## Related Flows

- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)

## Notes / TODOs

Detailed reference:
[project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).

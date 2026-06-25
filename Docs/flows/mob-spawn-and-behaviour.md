# Mob Spawn And Behaviour

## Purpose

Trace low-count mob spawning and behavior before mobs participate in ECS combat
through target proxies.

## Sequence

1. `MobSpawnerRoot` coordinates global spawn cap and spawn point requests.
2. `SpawnPoint` checks timing, overlap, and local spawn rules.
3. Mob prefab is instantiated or reused from a pool.
4. `MobRoot` validates Unity references and binds behavior, animation, health,
   status, and projectile attack helpers.
5. Mob behavior triggers update local events and selected movement/attack state.
6. Mob root registers as a combat target and pushes target proxy data.
7. Mob attacks submit projectile/AOE spawn requests through combat roots.
8. Presentation results update hurt/death feedback and target cleanup.

## Producers

`MobSpawnerRoot`, `SpawnPoint`, mob behavior triggers, `MobRoot`, and
`MobProjectileAttack`.

## Consumers

Scene actors, combat bridge, ECS simulation via target proxies and spawn events,
and presentation feedback.

## Contracts Used

- [Target Proxy](../contracts/target-proxy.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Combat Root API](../contracts/combat-root-api.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)

## Layer Boundaries Crossed

- [Scene And Authoring](../layers/scene-and-authoring.md)
- [Game Logic](../layers/game-logic.md)
- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

Mob movement and body collision stay in Physics2D. Combat hit detection uses
target proxies pushed before simulation.

## Failure / Edge Cases

Spawn overlap checks and caps prevent invalid overpopulation. Death presentation
must wait until target proxy cleanup is safe for current-frame result replay.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-004](../decisions/adr-004-target-proxy-collision.md)

## Notes / TODOs

Detailed references:
[mobs.md](../reference/game-logic/mobs.md),
[mob-behaviour.md](../reference/game-logic/mob-behaviour.md), and
[spawn-system.md](../reference/game-logic/spawn-system.md).

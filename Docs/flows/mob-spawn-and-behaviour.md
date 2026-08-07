# Mob Spawn And Behaviour

## Purpose

Trace low-count mob spawning and behavior before mobs participate in ECS combat
through target proxies.

Current status: the previous mob spawning implementation was removed so the next
spawning pass can start clean. Mob behavior and target proxy participation remain
owned by `MobRoot`.

## Sequence

1. A future mob spawning system chooses when and where to create low-count mob
   scene actors.
2. `MobRoot` validates Unity references and binds behavior, animation, health,
   status, and projectile attack helpers.
3. Mob behavior triggers update local events and selected movement/attack state.
4. Mob root registers as a combat target and pushes target proxy data.
5. Mob attacks submit projectile/AOE/targeted spawn requests through combat roots.
6. Presentation results update hurt/death feedback and target cleanup.

## Producers

Future mob spawning code, mob behavior triggers, `MobRoot`, and
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

Future spawn overlap checks and caps should prevent invalid overpopulation.
Death presentation must wait until target proxy cleanup is safe for
current-frame result replay.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-004](../decisions/adr-004-target-proxy-collision.md)

## Notes / TODOs

Detailed references:
[mobs.md](../reference/game-logic/mobs.md),
[mob-behaviour.md](../reference/game-logic/mob-behaviour.md), and
[spawn-system.md](../reference/game-logic/spawn-system.md).

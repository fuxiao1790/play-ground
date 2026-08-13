# Mob Spawn And Behaviour

## Purpose

Trace low-count Unity mob spawning, behavior, and ECS combat proxy lifecycle.

## Sequence

1. `SpawnController` applies its `SpawnBehaviour`, cap, placement, and
   `MobSpawnTable` choice.
2. `MobPool` rents a disabled `MobRoot`; `SpawnController` wires combat roots,
   target registry, optional VFX root, and target.
3. `SpawnController` submits proxy creation and records the disabled mob as an
   in-flight spawn. In-flight mobs count toward the cap.
4. Simulation creates the proxy. Presentation binds its companion and records
   confirmation on the mob.
5. On next `Update()`, `SpawnController` enables the confirmed mob. `MobRoot`
   then pushes proxy state, runs movement/behavior, and drives skills.
6. Mob attacks submit projectile/AOE/targeted requests through combat roots.
7. A tagged proxy reaching zero health produces a deferred despawn. The mob
   handles it next `Update()`, then `SpawnController` returns it to `MobPool`.

## Producers

`SpawnController`, `SpawnBehaviour`, `SpawnPlacement`, `SpawnPoint`,
`MobRoot`, and `MobProjectileAttack`.

## Consumers

`MobPool`, combat bridge, ECS target proxy simulation, presentation feedback,
and the spawn controller reclaim path.

## Contracts Used

- [Target Proxy](../contracts/target-proxy.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Combat Root API](../contracts/combat-root-api.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)

## Ordering / Timing Requirements

Mob movement and body collision stay in Physics2D. Combat target state pushes
before simulation after the mob is confirmed. Spawn confirmation and death
notifications push in presentation; actor actions occur on the following
`Update()`.

## Failure / Edge Cases

The pool does not return a mob for ordinary teardown deletion. Only the
combat-despawn event leads to `SoftDied`, preventing duplicate stack entries in
`MobPool`. A newly created proxy can be targetable for one frame before its
disabled mob appears.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-004](../decisions/adr-004-target-proxy-collision.md)
- [ADR-007](../decisions/adr-007-deferred-spawn-despawn-handshake.md)

## Notes / TODOs

Detailed references:
[mobs.md](../reference/game-logic/mobs.md),
[mob-behaviour.md](../reference/game-logic/mob-behaviour.md), and
[spawn-system.md](../reference/game-logic/spawn-system.md).

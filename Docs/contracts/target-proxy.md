# Target Proxy

## Purpose

Define the ECS-readable representation of targets for collision, tracking,
health/status aggregation, and presentation bridge lookup. Targets from all
factions share one proxy schema and one spatial hash.

## Produced By

[Combat Bridge](../layers/combat-bridge.md), with source data from
[Scene And Authoring](../layers/scene-and-authoring.md).

## Consumed By

[ECS Simulation](../layers/ecs-simulation.md) and
[Presentation And Feedback](../layers/presentation-and-feedback.md).

## Fields / Shape

Current proxy data includes:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction` — the target's **own** allegiance (`Player`, `Mob`, etc.); collision and tracking skip same-faction candidates (`self.Faction == target.Faction`)
- `Health`
- `Mana`
- `TargetStackEntry` buffer
- managed `TargetCompanion`
- compatibility `CombatTargetElement` in older code paths

## Guarantees

Simulation systems can read unmanaged proxy data without touching Unity
objects. Presentation bridge can resolve `TargetCompanion` after finalized
results exist.

Each actor root owns its resolved proxy `Entity` handle. Once non-null, that
handle remains valid until the actor fires its proxy-despawn event and clears
the handle. Bridge hot paths trust this lifetime contract instead of calling
`EntityManager.Exists` per actor; an invalid non-null handle before despawn is a
lifecycle bug and must fail fast.

## Restrictions

Simulation jobs must not read managed `TargetCompanion`. New collision and
tracking work should use target proxy entities, not live colliders or legacy
target snapshot buffers.

## Lifetime

Actor registration enqueues a create event that seeds `Health`/`Mana` Current,
Max, and RegenPerSecond from the root's `Resource` values. The
`TargetProxyCreateApplySystem` creates the proxy in a later simulation tick, so
the actor's proxy handle resolves then rather than during registration. The root
owns initial values, Max, and regen-rate; ECS owns runtime Current (damage,
spending, and regen). Roots enqueue Max/regen, shape, and position updates;
`TargetProxyUpdateApplySystem` applies them without resetting Current, then the
root mirrors Current back for presentation. Actor teardown enqueues deletion;
`TargetProxyDeleteApplySystem` applies it after current-frame proxy users are
safe.

## Ordering

Create and update events apply in `SimulationSystemGroup` before
`TargetSpatialHashSystem`, so collision and tracking read current proxy data.
Delete events apply in `PresentationSystemGroup` after `CombatApplyBridge`,
preserving current-frame hit/result replay safety.

## Related Layers

- [Scene And Authoring](../layers/scene-and-authoring.md)
- [Combat Bridge](../layers/combat-bridge.md)
- [ECS Simulation](../layers/ecs-simulation.md)
- [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Related Flows

- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)
- [Runtime Frame](../flows/runtime-frame.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

## Notes / TODOs

`TargetFaction` is set once at proxy creation to the target's own allegiance
(not the firing faction). The friendly-fire gate uses a single inequality test
in the narrow phase rather than per-faction spatial-hash buckets.

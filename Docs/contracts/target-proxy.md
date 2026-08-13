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
- optional `DespawnOnDeathTag`
- managed `TargetCompanion`
- compatibility `CombatTargetElement` in older code paths

## Guarantees

Simulation systems can read unmanaged proxy data without touching Unity
objects. Presentation bridge can resolve `TargetCompanion` after finalized
results exist.

## Restrictions

Simulation jobs must not read managed `TargetCompanion`. New collision and
tracking work should use target proxy entities, not live colliders or legacy
target snapshot buffers.

## Lifetime

Actor registration enqueues a create event that seeds `Health`/`Mana` Current,
Max, RegenPerSecond, and the optional death-despawn flag from the root. The
simulation create system returns `TargetProxySpawnResult`; in the same frame's
`PresentationSystemGroup`, `CombatActorSpawnBridge` resolves the token, binds
`TargetCompanion`, assigns the actor's handle, and calls `OnCombatSpawned`.
Actors act on that push in their next `Update()`. The root owns initial values,
Max, and regen rate; ECS owns runtime Current. Roots enqueue Max/regen, shape,
and position updates; `TargetProxyUpdateApplySystem` applies them without
resetting Current. Health reaching zero on a tagged proxy emits both
`CombatDespawnEvent` and `TargetProxyDeleteEvent`; presentation pushes the
despawn before `TargetProxyDeleteApplySystem` destroys the entity.

## Ordering

Create and update events apply in `SimulationSystemGroup` before
`TargetSpatialHashSystem`, so collision and tracking read current proxy data.
Presentation order is `CombatApplyBridge`, `CombatActorSpawnBridge`,
`CombatDespawnBridge`, then `TargetProxyDeleteApplySystem`; this preserves
killing-hit replay and keeps companions available to both lifecycle bridges.

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

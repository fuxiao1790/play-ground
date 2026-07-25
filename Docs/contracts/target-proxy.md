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

## Restrictions

Simulation jobs must not read managed `TargetCompanion`. New collision and
tracking work should use target proxy entities, not live colliders or legacy
target snapshot buffers.

## Lifetime

Actor registration creates the proxy and seeds `Health`/`Mana` Current, Max, and
RegenPerSecond from the root's `Resource` values. The root owns initial values,
Max, and regen-rate; ECS owns runtime Current (damage, spending, and regen).
Roots push Max/regen changes without resetting Current, then mirror Current back
for presentation. Actor updates also push shape/position. Actor teardown queues
deletion after current-frame proxy users are safe.

## Ordering

Proxy push must happen before simulation collision/tracking reads. Proxy
deletion should occur after current-frame hit/result replay safety.

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

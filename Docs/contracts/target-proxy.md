# Target Proxy

## Purpose

Define the ECS-readable representation of player and mob targets for collision,
tracking, health/status aggregation, and presentation bridge lookup.

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
- `TargetFaction`
- `TargetHealth`
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

Actor registration creates the proxy. Actor updates push shape/position.
Actor teardown queues deletion after current-frame proxy users are safe.

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

TODO: verify whether all `CombatTargetElement` usages are legacy or whether any
current tests still require it as an active contract.

# Target Proxy Lifecycle

## Purpose

Show how GameObject actors become ECS-readable targets without making jobs read
live Unity objects.

## Sequence

1. Actor root implements `ICombatTarget`.
2. Actor registers with the owning combat root target registry.
3. Registry creates an ECS target proxy entity.
4. Actor stores the proxy entity handle.
5. Actor pushes position, shape, faction, and health/status seed data.
6. Simulation reads unmanaged proxy components for tracking, collision, damage,
   and status.
7. Presentation bridge resolves managed companion after finalize.
8. Dead or disabled actor queues proxy deletion and deletes at the safe actor
   frame boundary.

## Producers

Player roots, mob roots, `CombatTargetRegistry`, and `CombatTargetProxy`.

## Consumers

Projectile tracking/collision, AOE collision, combat apply/finalize, status
processing, and presentation bridge.

## Contracts Used

- [Target Proxy](../contracts/target-proxy.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)

## Layer Boundaries Crossed

- [Scene And Authoring](../layers/scene-and-authoring.md) to
  [Combat Bridge](../layers/combat-bridge.md)
- [Combat Bridge](../layers/combat-bridge.md) to
  [ECS Simulation](../layers/ecs-simulation.md)
- [ECS Simulation](../layers/ecs-simulation.md) to
  [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

Proxy push happens before simulation reads. Proxy deletion is delayed so hit and
presentation data from the current frame can resolve safely.

## Failure / Edge Cases

Simulation must tolerate missing/dead proxies by ignoring invalid target data.
Managed companion reads are presentation-only.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-004](../decisions/adr-004-target-proxy-collision.md)

## Notes / TODOs

TODO: verify exact proxy delete timing with current actor `LateUpdate()` and ECS
presentation order.

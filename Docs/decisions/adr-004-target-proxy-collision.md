# ADR-004: Target Proxy Collision

## Status

Accepted

## Context

Projectile and AOE collision need to scale beyond live Physics2D trigger
callbacks while still interacting with GameObject actors.

## Decision

Represent player and mob targets as ECS proxy entities. Actors push position,
shape, faction, health/status seed data, and a managed companion reference.
Simulation reads unmanaged proxy data. Presentation resolves the managed
companion only after finalization.

## Consequences

Collision and tracking avoid live Collider/Transform reads. Actor roots still
own GameObject movement and presentation. Proxy lifetime must be carefully
ordered around simulation and presentation.

## Alternatives Considered

- Live Physics2D trigger callbacks for high-count hits: rejected for scale.
- ECS-only actors: rejected for current scope.
- Managed target lookup inside jobs: rejected for job safety.

## Related Docs

- [Target Proxy](../contracts/target-proxy.md)
- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)

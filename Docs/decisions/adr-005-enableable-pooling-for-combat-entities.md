# ADR-005: Enableable Pooling For Combat Entities

## Status

Accepted

## Context

Projectiles and AOEs churn heavily. Repeated create/destroy or add/remove
component operations cause structural changes and sync points.

## Decision

Keep reusable projectile/AOE archetypes stable and despawn by disabling
enableable state such as `Active`, collision-active tags, render-active tags,
timed spawn, and lifetime where appropriate.

## Consequences

Hot despawn avoids structural churn. Apply systems search disabled slots before
cold creation. Archetype design must include reusable component sets up front,
including optional timed-spawn data on projectile and lingering AOE entities.
The runtime now uses three combat archetypes: projectile, impact AOE, and
lingering AOE. Removing the former timed variants adds two small timed
spawn components to projectile and lingering chunks, while keeping impact AOE
chunks lean with no lifetime, pulse VFX, contact gates, or timed-spawn data.

## Alternatives Considered

- Destroy entities on despawn: rejected for high churn.
- Add/remove timed-spawn components during reuse: rejected because it changes
  archetypes in hot paths.

## Related Docs

- [ECS Simulation](../layers/ecs-simulation.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [ecs-notes.md](../reference/simulation/ecs-notes.md)

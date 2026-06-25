# ADR-001: Hybrid GameObject And ECS Runtime

## Status

Accepted

## Context

The project needs authored 2D gameplay and very high-count combat effects.
Player, mobs, walls, camera, and debug UI benefit from Unity GameObjects and
Physics2D. Projectiles, AOEs, transient hits, status, and VFX requests need data
locality, pooling, and job-friendly processing.

## Decision

Use GameObjects/MonoBehaviours for low-count authored gameplay and ECS/DOTS for
high-count combat simulation.

## Consequences

The boundary must stay explicit. Scene objects submit intent and presentation
updates. ECS owns scalable combat state, pooling, collision, status aggregation,
and render/VFX request data.

## Alternatives Considered

- One GameObject per projectile/AOE: rejected because high-count combat would
  collapse under Unity object and Physics2D overhead.
- Full ECS actors: rejected for now because authored player/mob movement,
  animation, and scene composition are still easier and lower risk on
  GameObjects.

## Related Docs

- [Scene And Authoring](../layers/scene-and-authoring.md)
- [ECS Simulation](../layers/ecs-simulation.md)
- [Runtime Frame](../flows/runtime-frame.md)

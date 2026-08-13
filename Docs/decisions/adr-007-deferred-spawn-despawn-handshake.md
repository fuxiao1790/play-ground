# ADR-007: Deferred Spawn/Despawn Handshake

## Status

Accepted

## Context

Low-count GameObject actors need ECS-readable proxy state, while ECS simulation
must remain unmanaged. Immediate actor activation could run a mob without a
proxy; immediate death handling could hide an actor before presentation replayed
its killing hit. Player proxy death must not imply player actor despawn.

## Decision

Use a three-phase lifecycle protocol:

```text
actor Update() -> SimulationSystemGroup -> PresentationSystemGroup -> next Update()
```

For spawn, a disabled actor submits `TargetProxyCreateEvent`. Simulation creates
the proxy and writes `TargetProxySpawnResult`; `CombatActorSpawnBridge` binds
`TargetCompanion`, assigns the handle, and calls `OnCombatSpawned`. The actor
goes live on next `Update()`.

For despawn, `CombatDespawnOnDeathSystem` observes `Health <= 0` only on
`DespawnOnDeathTag` proxies. It writes `CombatDespawnEvent` and
`TargetProxyDeleteEvent`. Presentation replays combat feedback, pushes
`OnCombatDespawned`, then destroys the proxy. The actor hides and performs pool
reclaim work on next `Update()`.

`PresentationSystemGroup` is the push phase because it is the managed companion
boundary and runs after simulation. Exact ordering against MonoBehaviour
`LateUpdate()` remains pending confirmation from a project frame-marker trace;
the protocol does not require an actor to consume a push in that same frame.

## Consequences

- An actor never runs live without a confirmed proxy.
- Killing-hit feedback reaches the actor before its despawn notification.
- Proxy destruction occurs in the same presentation phase, so a second
  simulation pass cannot emit duplicate death events.
- Spawn decision, prefab knowledge, placement, pooling, and actor presentation
  remain in GameObject code. ECS owns only proxy runtime state and the tagged
  death decision.
- ADR-001 remains in force: authored movement, animation, and scene composition
  stay on GameObjects.
- Proxy entities are created/destroyed per mob lifecycle. Unlike ADR-005
  projectile/AOE entities, they are not pooled: proxy pooling would need active
  gates, query exclusions, reset rules, and reuse validation for an unmeasured
  allocation path.

## Accepted Windows

- For one frame, a proxy can exist while its actor remains hidden. A
  `TargetProxyPendingTag` plus a spatial-hash `WithNone` gate would close this;
  it is deliberately not built.
- For one frame, an actor can remain visible while its destroyed proxy is gone.
- Spawn latency is one frame. `SpawnController` counts pending confirmations so
  this cannot overshoot its cap.

## Alternatives Considered

- Activate actors before proxy confirmation: rejected because they can simulate
  a frame without a combat target.
- Notify actors from every delete event: rejected because teardown deletes would
  return a mob to `MobPool` twice.
- Pool proxy entities like projectiles/AOEs: rejected for now; see consequences.

## Related Decisions

- [ADR-001](./adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-005](./adr-005-enableable-pooling-for-combat-entities.md)

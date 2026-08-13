# Target Proxy Lifecycle

## Purpose

Show deferred GameObject-to-ECS target proxy lifecycle without simulation code
reading live Unity objects.

## Spawn Handshake

```text
frame N Update()        actor or SpawnController submits TargetProxyCreateEvent
frame N Simulation      TargetProxyCreateApplySystem creates proxy and writes
                        TargetProxySpawnResult { Token, Proxy }
frame N Presentation    CombatActorSpawnBridge resolves token, adds
                        TargetCompanion, assigns handle, calls OnCombatSpawned
frame N+1 Update()      actor consumes confirmation; SpawnController enables mob
```

Pooled mobs rent disabled. They cannot run an actor frame before confirmation.
The proxy can exist for one frame before its actor is enabled; see ADR-007 for
the deliberately unbuilt pending-proxy gate.

## Despawn Handshake

```text
frame N Simulation      Health <= 0 plus DespawnOnDeathTag emits
                        CombatDespawnEvent and TargetProxyDeleteEvent
frame N Presentation    CombatApplyBridge replays killing hit
                        CombatDespawnBridge clears handle and calls
                        OnCombatDespawned (record only)
                        TargetProxyDeleteApplySystem destroys proxy
frame N+1 Update()      MobRoot hides, stops physics, unregisters, raises
                        SoftDied; SpawnController returns mob to MobPool
```

The player has no `DespawnOnDeathTag`; actor-side player teardown still owns its
own delete intent. A destroyed proxy cannot be seen by a second simulation pass,
so the death lane needs no per-entity dedupe state.

## Producers

Actor roots, `SpawnController`, `CombatTargetRegistry`, and
`CombatTargetProxy` produce create, update, or delete intent. ECS produces
spawn-result and combat-despawn outcomes.

## Consumers

`TargetProxyCreateApplySystem`, `TargetProxyUpdateApplySystem`, collision and
combat simulation consume unmanaged data. `CombatActorSpawnBridge`,
`CombatDespawnBridge`, and `TargetProxyDeleteApplySystem` consume lifecycle
outcomes in presentation.

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

Create and update apply before spatial hashing. Presentation runs lifecycle
bridges after finalized combat replay and before proxy deletion. Exact ordering
between MonoBehaviour `LateUpdate()` and `PresentationSystemGroup` remains
pending frame-marker trace validation; actor protocol relies only on consuming
pushes in the next `Update()`.

## Failure / Edge Cases

Cancelling a create before its result arrives removes its token. The spawn bridge
then destroys the unbound proxy instead of leaking an entity without a companion.
Simulation ignores missing or destroyed proxies; managed companion access stays
presentation-only.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-004](../decisions/adr-004-target-proxy-collision.md)
- [ADR-007](../decisions/adr-007-deferred-spawn-despawn-handshake.md)

# Runtime Frame

## Purpose

Show the cross-layer order for one gameplay frame.

## Sequence

1. Scene actors update input, movement intent, cooldowns, and behavior.
2. Player and mob roots enqueue target-proxy create, position, shape, and
   resource-update events. `TargetProxyCreateApplySystem` and
   `TargetProxyUpdateApplySystem` consume them in `SimulationSystemGroup` before
   `TargetSpatialHashSystem`. Create results push in the same frame's
   presentation phase; an actor acts on its confirmed handle in next `Update()`.
3. ECS lifetime systems expire old projectile/AOE entities and any targeted
   entity that outlives its fail-safe lifetime.
4. Timed spawn systems enqueue child projectile/AOE/targeted events.
5. Projectile systems track, move, expire contact gates, and collide.
6. AOE systems emit pulse VFX and collide on their tick intervals.
7. Targeted resolve walks chains, emitting one hit and one line segment per link,
   and expires each chain when its walk ends.
8. Combat apply/finalize aggregates hit data into ECS health/status and freezes
   compact results.
9. Status processing may enqueue detonation spawn events.
10. Spawn expansion drains managed and ECS event queues into commands.
11. Apply systems reuse disabled slots or cold-create overflow.
12. Render prep writes matrices.
13. `CombatDespawnOnDeathSystem` turns zero health on tagged proxies into
   `CombatDespawnEvent` and `TargetProxyDeleteEvent`. Player teardown may also
   enqueue deletion.
14. Presentation replays combat results, pushes spawn and despawn outcomes to
   actors, applies proxy deletion, then dispatches VFX and render batches.

## Producers

Scene actors, combat bridge, projectile systems, AOE systems, targeted resolve,
status systems, render prep systems, and VFX producers.

## Consumers

ECS simulation systems, combat bridge buffers, actor roots, VFX dispatch, and
batched render submission.

## Contracts Used

- [Target Proxy](../contracts/target-proxy.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [VFX Requests](../contracts/vfx-requests.md)
- [Render Batch Data](../contracts/render-batch-data.md)

## Layer Boundaries Crossed

- [Scene And Authoring](../layers/scene-and-authoring.md) to
  [Combat Bridge](../layers/combat-bridge.md)
- [Combat Bridge](../layers/combat-bridge.md) to
  [ECS Simulation](../layers/ecs-simulation.md)
- [ECS Simulation](../layers/ecs-simulation.md) to
  [Presentation And Feedback](../layers/presentation-and-feedback.md)

## Ordering / Timing Requirements

Target-proxy create and update events apply before spatial hashing and collision
reads proxy data. Spawn apply runs after movement/collision, so newly spawned
entities first simulate next frame. Presentation pushes confirmed proxy creates
and despawns before deletion; actors consume those pushes in next `Update()`.

## Failure / Edge Cases

Invalid targets should delete proxies after current-frame users are done.
Underwarmed pools fall back to cold creation. Visual VFX events may be capped.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-005](../decisions/adr-005-enableable-pooling-for-combat-entities.md)

## Notes / TODOs

TODO: verify actor `LateUpdate()` ordering against ECS presentation systems.
Planned stock-Entities order is documented above; frame-marker trace validation
is pending.

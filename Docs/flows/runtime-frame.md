# Runtime Frame

## Purpose

Show the cross-layer order for one gameplay frame.

## Sequence

1. Scene actors update input, movement intent, cooldowns, and behavior.
2. Player and mob roots push target proxy position and shape.
3. ECS lifetime systems expire old projectile/AOE entities.
4. Timed spawn systems enqueue child projectile/AOE events.
5. Projectile systems track, move, expire contact gates, and collide.
6. AOE systems emit pulse VFX and collide on their tick intervals.
7. Combat apply/finalize aggregates hit data into ECS health/status and freezes
   compact results.
8. Status processing may enqueue detonation spawn events.
9. Spawn expansion drains managed and ECS event queues into commands.
10. Apply systems reuse disabled slots or cold-create overflow.
11. Render prep writes matrices.
12. Actor roots delete queued invalid target proxies.
13. Presentation systems dispatch combat results, VFX, and render batches.

## Producers

Scene actors, combat bridge, projectile systems, AOE systems, status systems,
render prep systems, and VFX producers.

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

Target proxy push must happen before collision reads proxy data. Apply runs
after movement/collision, so newly spawned entities first simulate next frame.
Managed companion resolution happens after combat results are finalized.

## Failure / Edge Cases

Invalid targets should delete proxies after current-frame users are done.
Underwarmed pools fall back to cold creation. Visual VFX events may be capped.

## Related Decisions

- [ADR-001](../decisions/adr-001-hybrid-gameobject-ecs-runtime.md)
- [ADR-005](../decisions/adr-005-enableable-pooling-for-combat-entities.md)

## Notes / TODOs

TODO: verify actor `LateUpdate()` ordering against ECS presentation systems.

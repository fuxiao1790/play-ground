# Remove Projectile Line-Segment Trails Plan

## Summary

Remove projectile trail implementation that approximates trails by emitting many
distance-gated `LineSegment` VFX events. Delete whole projectile-only path:

```text
BasicAttackPrefab trail fields
  -> RuntimeProjectileDefinition trail fields
  -> ProjectileSpawnCommand trail fields
  -> ProjectileTrailVfxComponent on every projectile
  -> ProjectileMovementSystem distance gate
  -> LineSegmentVfxEvent queue
```

Do not replace it yet. Projectiles continue using batched sprite rendering with
no trail. Future proper trail design starts from clean ownership and data-shape
decisions instead of preserving compatibility with segment emission.

`LineSegment` itself stays. Targeted chain links still use
`VfxDataShape.LineSegment`, `VfxEmit.EnqueueLineSegment`, shared queue/bucketing,
GPU buffers, `PlagueLink.vfx`, and corresponding tests.

## Architectural Decisions

1. **Remove only projectile trail producer.** User named line-segment VFX trail
   and described many line segments as fake trail. Targeted chain links are
   discrete directional connections, not trails, so removing shared
   `LineSegment` infrastructure would destroy unrelated targeted behavior.
2. **Delete data path, not disable it.** Remove trail fields, registration,
   validation, spawn payload, ECS component, movement emission, authored
   bindings, graphs, and exclusive textures. Do not retain zero ids, dormant
   components, obsolete serialized fields, feature flags, or compatibility
   adapters.
3. **Restore movement ownership.** `ProjectileMovementSystem` only integrates
   position and refreshes collision bounds. It no longer resolves VFX singleton,
   writes native queue, or extends VFX producer dependency.
4. **Keep targeted LineSegment contract intact.** Shared enum/event/buffer
   contracts and dispatch resource lifetime remain because
   `TargetedResolveSystem` is still active producer.
5. **Retain AgentVFX compatibility coverage.** Point generic graph-read test at
   surviving `Assets/Vfx/LineSeg/PlagueLink.vfx`, not deleted projectile graph.

## Constraints And Invariants

### Ownership and data-flow boundary

- Authoring lives in `BasicAttackPrefab`; compiled managed data lives in
  `RuntimeProjectileDefinition`; unmanaged spawn snapshot lives in
  `ProjectileSpawnCommand`; projectile state lives on pooled ECS entities
  (`Docs/layers/game-logic.md`, `Docs/layers/ecs-simulation.md`,
  `Docs/reference/simulation/projectile-system.md`).
- Remove trail values from every layer in same change. Leaving one layer creates
  stale source of truth or wasted copy.
- Gameplay behavior cannot depend on visual requests
  (`Docs/layers/presentation-and-feedback.md`, `Docs/flows/vfx-dispatch.md`).
  Removing trail emission must not alter velocity, collision bounds, arming,
  lifetime, collision, damage, spawn expansion, or render identity.

### ECS lifecycle and archetype shape

- Both discrete and continuous projectile archetypes contain
  `ProjectileTrailVfxComponent` today, and pooled slots keep/reset it on reuse
  (`ProjectileDiscreteSpawnApplySystem.cs`,
  `ProjectileContinuousSpawnApplySystem.cs`,
  `ProjectileEcsComponents.cs`).
- Remove component declaration, both archetype entries, handles, native-array
  access, `WriteCommon` argument, and reset write together. Update manual test
  archetypes in same change.
- ECS lifecycle comments must change with lifecycle changes
  (`Docs/reference/simulation/ecs-notes.md`). Deleting component deletes its
  lifecycle comment; no replacement component is introduced.

### Concurrency and VFX queue ownership

- VFX producers write with `NativeQueue<T>.ParallelWriter`, then combine job
  handles into `CombatAoeVfxDispatchSingleton.ProducerHandle`; dispatcher
  completes it before draining (`Docs/reference/simulation/vfx-system.md`,
  `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`).
- Projectile movement must stop acquiring `PendingLineSegmentSpawns` and stop
  combining its handle into VFX producer handle.
- `TargetedResolveSystem` remains LineSegment producer. Therefore do not remove
  `LineSegmentVfxEvent`, queue allocation/disposal, bucket lists/job,
  `DrainAndDispatchLineSegment`, `LineSegmentVfxResources`, dispatcher upload,
  validation contract, or `VfxEmit.EnqueueLineSegment`.

### Performance and allocation

- Projectile simulation and VFX dispatch are hot paths; combat paths must remain
  allocation-light (`Docs/reference/simulation/ecs-notes.md`,
  `Docs/reference/simulation/vfx-system.md`).
- Removal eliminates 16 bytes of trail state per projectile, component stream
  access in movement/apply jobs, per-projectile distance checks, queue writes,
  staging copies, and projectile-trail GPU uploads.
- Add no replacement allocation, managed lookup, structural change, event lane,
  or per-frame compatibility check.

### Asset identity and serialization

- `MagicBolt.prefab` and `MagicBolt2.prefab` reference
  `MagicBoltTrail.vfx`; `FireArrow.prefab` references `FireArrowTrail.vfx`.
- `MagicBoltTrail.vfx` exclusively references
  `magic-bolt-2-trail.png`; `FireArrowTrail.vfx` exclusively references
  `fire-arrow-trail.png`. `fire-arrow-trail-v2.png` has no repository reference.
- Remove asset plus `.meta` pairs and four obsolete serialized trail entries
  from affected projectile prefabs. Preserve all projectile sprite/material,
  hurtbox, and sound data.
- Preserve `PlagueLink.vfx` and its prefab reference.

### Testing evidence

- Agents do not run Unity tests. User runs named EditMode/PlayMode coverage and
  exports XML under `Logs/` per `Docs/testing.md`.
- Test result claims require review of those XML files. Console output or logs
  do not substitute.

## Mechanisms Reused Vs Introduced

### Reused

- Batched projectile sprite rendering remains only projectile visual path.
- Existing projectile spawn expansion, pooled archetypes, movement integration,
  collision bounds refresh, collision, lifetime, and arming systems remain.
- Existing LineSegment request/dispatch path remains for targeted links.
- `PlagueLink.vfx` becomes fixture for generic AgentVFX read compatibility.

### Introduced

- No runtime mechanism, data type, adapter, fallback, or replacement trail.
- Only test expectation changes and asset cleanup accompany removal.

## Design Validation

| Invariant | Result |
|---|---|
| Projectile simulation independent from presentation | Movement job no longer reads VFX singleton or writes VFX queue. |
| Projectile gameplay unchanged | Position integration and bounds recomputation stay byte-for-byte equivalent in job body. |
| Pooled archetypes remain internally consistent | Trail component is removed from declarations, handles, array access, writes, and test fixtures together. |
| Targeted links remain functional | Shared LineSegment shape/queue/dispatcher/emitter and `PlagueLink.vfx` stay; targeted tests remain in validation set. |
| No stale authored source | Script fields, runtime fields, validation branch, registration, prefab YAML, graph assets, and exclusive textures are removed. |
| Hot path becomes smaller | Per-entity state, trail math, queue writes, producer-handle merge, staging, and uploads disappear for projectiles. |
| Future design starts cleanly | No dormant compatibility representation remains to constrain new trail architecture. |

## Minimal/Additive Vs Refactor Comparison

### Minimal/additive approach

- Resulting data flow: keep trail fields/component/queue path, set graph ids to
  zero or clear current prefab assignments.
- New concepts/types introduced: possibly disable flag or placeholder trail
  mode.
- Copies/translations added: existing trail values continue flowing through
  managed definitions, spawn commands, and ECS state despite producing nothing.
- Long-term cost: dead authoring API and per-projectile memory remain; future
  trail implementation must coexist with or dismantle old model later.

### Refactor approach

- Resulting data flow: projectile authoring compiles directly to render/gameplay
  spawn data; movement only moves and refreshes bounds.
- Existing concepts/types changed or removed: projectile trail fields,
  `ProjectileTrailVfxComponent`, registration/validation, segment emission,
  prefab bindings, trail graphs, and exclusive textures.
- Copies/translations removed or avoided: all trail copies across authoring,
  runtime, command, pooled entity, queue, staging, and GPU upload disappear.
- Long-term benefit: one fewer projectile data path, smaller archetypes, clearer
  movement ownership, clean base for proper trail design.

### Decision

- **Choose refactor.** Full deletion matches requested removal and avoids
  carrying fake-trail structure into future design.

## Task Index

1. [001-remove-authoring-and-snapshot-path.md](001-remove-authoring-and-snapshot-path.md)
   - remove projectile trail authoring, validation, registration, and command
   fields.
2. [002-remove-ecs-emission-path.md](002-remove-ecs-emission-path.md)
   - remove pooled trail component and segment emission from movement.
3. [003-clean-assets-and-graph-fixture.md](003-clean-assets-and-graph-fixture.md)
   - remove projectile trail assets/bindings and retarget AgentVFX fixture.
4. [004-update-tests-and-docs.md](004-update-tests-and-docs.md)
   - align fixtures/docs and validate projectile behavior plus targeted links.

## Open Questions And Considerations

- No replacement trail type, renderer, buffer, graph, or authoring field belongs
  in this change. Future trail work needs separate design.
- AgentVFX server was unreachable during planning, so graph internals were not
  inspected. This does not block deletion: asset references establish projectile
  graph ownership, and generic compatibility test can use surviving
  `PlagueLink.vfx`. Confirm that fixture in Unity during implementation.
- No pre-planning `.agent/remove-projectile-line-segment-trails/info.md` existed.


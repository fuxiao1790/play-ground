# 005 — Migrate tests off shared-component batch id

Any test that constructs entities with `AddSharedComponent<CombatRenderBatchId>`
or queries with `SetSharedComponentFilter` must change, since batch id is now a
plain `IComponentData`.

## Known touch points
- [ProjectileSpawnPipelineTests.cs:387-410](../../Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs#L387-L410) — manual archetype + `AddSharedComponent(entity, new CombatRenderBatchId { Value = 1 })`.
  Add `typeof(CombatRenderBatchId)` to the archetype; replace `AddSharedComponent`
  with `SetComponent`.
- [BareMinimumPrototypePlayModeTests.cs:827-830](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs#L827-L830) — query
  `WithAll<CombatRenderBatchId>` + `SetSharedComponentFilter(... typeId)`. Replace
  the shared filter with a manual count of entities whose
  `CombatRenderBatchId.Value == typeId` (iterate the query / chunks), or drop the
  filter if the test only needs a total.
- [ProjectileCollisionSimulationTests.cs:383](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs#L383) and any other manual
  archetype including render tags — add `typeof(CombatRenderBatchId)` if the
  entity flows through a reuse/render query that now expects the component.

## New assertions to add (protect the central invariant)
- **No stale batch id on reuse:** spawn an entity of render type A, despawn it,
  spawn type B into the reused slot, assert its `CombatRenderBatchId.Value == B`.
  Covers projectile (basic + child) and AOE.

## Acceptance criteria
- Full PlayMode suite compiles and passes.
- The stale-batch-id reuse test exists and passes.

## Depends on
002, 003, 004.

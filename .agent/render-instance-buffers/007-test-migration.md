# 007 — Migrate PlayMode tests off the removed component

Tests must observe real gameplay data, so compute the expected matrix from the
still-present `CombatKinematicsComponent` + `CombatRenderComponent` via
`CombatRenderMatrixUtility.MatrixFor` (identical to what render submits) rather
than reading a component or the internal buffer.

## Changes

- `Assets/Tests/PlayMode/AoePlayModeTests.cs` — `FirstScopedAoeRenderMatrix`
  (~L717-737): query `AoeIdentityComponent` + `CombatKinematicsComponent` +
  `CombatRenderComponent`; for the matching faction return
  `CombatRenderMatrixUtility.MatrixFor(kin, render)`. Drop the
  `CombatRenderElement` query type and `GetComponentData<CombatRenderElement>`.
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs` (~L828): drop
  `ComponentType.ReadOnly<CombatRenderElement>()` from the render-count query
  (the count is driven by `CombatRenderBatchId` + `CombatRenderActiveTag`).
  Optional: also assert against
  `CombatBatchedRenderSystem.LastActiveProjectileCount`.
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` (~L384, L401, L427):
  remove `typeof(CombatRenderElement)` from archetype/query type lists for parity
  with the new apply archetypes.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` (~L378): remove
  `typeof(CombatRenderElement)`.

## Acceptance criteria

- No test references `CombatRenderElement`.
- `AoePlayModeTests` matrix assertions pass (end-to-end spawn → prepare → matrix).
- `BareMinimumPrototypePlayModeTests.RenderInstanceCount`, `ProjectileSpawnPipelineTests`
  (pooling parity), and `ProjectileCollisionSimulationTests` pass.

## Dependencies

Depends on 001 (`MatrixFor`) and 005/006 (archetypes) for parity.

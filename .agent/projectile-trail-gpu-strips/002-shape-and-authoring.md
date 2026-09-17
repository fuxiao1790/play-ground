# 002 — Add shape and graph definition

Scope: VFX shape identity, graph registration, and projectile authoring
contract. Complexity: medium. Dependency: 001.

Work:

1. Append `ProjectileTrail = 3` to `VfxDataShape`, preserving ordinals 0–2
   and existing encoded ids. Add `VfxDataShapeTable` names for exposed
   `TrailPositions` (`GraphicsBuffer`, float2 elements), `TrailWidths`
   (`GraphicsBuffer`, float elements), `ResolvedStripIndices`
   (`GraphicsBuffer`, uint elements), `PointLifetime` (`float`), plus common
   `SpawnCount` (`int`) and `OnSpawn`. Reject unknown shapes rather than
   returning Circular buffers.
2. Add `ProjectileTrailVfxDefinition` ScriptableObject: graph asset,
   `MaxStrips > 0`, `ParticlesPerStrip >= 2`, and
   `PointLifetimeSeconds > 0`. The common compute shader reference belongs
   on `CombatVfxRoot`; no per-projectile compute asset or GameObject.
3. Replace `BasicAttackPrefab`'s projectile `TrailEffect` graph plus
   `TrailEffectShape` selector with one definition reference. Keep existing
   trail width and step distance. Update compile, recursive registration,
   runtime snapshot, and validation paths. Validate definition's graph,
   positive settings, and a `ProjectileTrail` contract. A targeted link still
   requires `LineSegment`.
4. Add `CombatVfxRoot.RegisterProjectileTrail(definition)`. Continue keying
   registration by graph asset; reject a second definition with the same asset
   and different settings. Create one root-owned resource per graph and set
   graph `PointLifetime` from definition. Clear all definition identity and
   resource state in `ResetDispatcher` and teardown.
5. Include new shape in root owner-list indexing, validation, and diagnostics.
   Do not let `Register(asset, ProjectileTrail)` bypass required definition.
   Keep other shapes on existing `Register(asset, shape)` API.

Acceptance:

- Existing shape IDs and targeted link registration behave identically.
- Projectile trail authoring has one graph/lifetime/capacity source.
- Same graph with conflicting definitions fails clearly before runtime use.
- Missing compute shader or graph contract causes registration failure and
  visual trail id 0; gameplay spawn remains valid.
- Resource creation/disposal has one owner and no per-projectile allocations.

Tests: user runs **EditMode** `CombatVfxRootRegistrationTests`
(`ProjectileTrailId_RetainsShapeAndLocalIndex`,
`RegisterProjectileTrail_RejectsConflictingDefinition`,
`RegisterProjectileTrail_RejectsMissingContract`) and
`ProjectileAuthoringEditModeTests` (trail-definition validation cases).
Export XML under `Logs/`; agent reviews it.

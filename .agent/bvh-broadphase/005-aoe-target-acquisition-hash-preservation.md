# 005 - AOE And Target Acquisition Spatial Hash Preservation

## Change

Keep impact/lingering AOE collision and all target acquisition on existing
spatial hashes. This task is a preservation and regression gate, not a
migration.

Preserve AOE cell traversal, duplicate suppression, current per-tick hit cap,
faction/AABB/exact tests, cooldowns, consequence payloads, VFX, and lifecycle.

Preserve targeted chain/external anchor acquisition through AOE occupied cells:
cell-range traversal, bounded nearest list, distance then snapshot-index
ordering, previous-target exclusion, external acquisition timing, and chain
behavior.

Preserve projectile tracking acquisition/refresh through tracking cells and
tracked-target ID lookup, including directional cell bands and target-index
validation.

## Acceptance Criteria

- Impact and lingering AOE jobs read retained AOE occupied cells, not BVH.
- `AoeCollisionCore` keeps existing duplicate suppression and
  `CollisionConstants.MaxAoeTargetsPerTick` behavior.
- No all-hits behavior change, streaming leaf path, or fixed-cap removal occurs.
- `TargetedAcquisition.Snapshot` keeps occupied-cell view and targeted selection
  performs no BVH traversal.
- Nearest/rank/falloff/exclusion, chain timing/cap, external mana gate,
  `HasAcquiredTarget`, hit/VFX payloads, and walk-end lifecycle remain unchanged.
- `ProjectileTrackingSystem` keeps tracking-cell and ID-map acquisition; it gains
  no BVH tree/view/workspace dependency.
- `AoeSimulationTests`, relevant `AoePlayModeTests`,
  `TargetedResolveEditModeTests`, `TargetedSkillPlayModeTests`, and
  `ProjectileTrackingSimulationTests` remain behavioral regression coverage for
  retained hash paths.
- Static diff review proves no AOE, targeted, or tracking BVH consumer was
  introduced.

## Dependencies

- 002 - Target Broadphase Owner And Build retains AOE/tracking hash containers.
- 003 - Discrete Projectile BVH Queries.

## Scope / Complexity

Low. Preservation gate preventing unrelated AOE/acquisition behavior changes.

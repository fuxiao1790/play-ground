# 004 - Continuous Spatial Hash Preservation

## Change

Keep `ProjectileContinuousCollisionSystem` on existing spatial-hash broadphase.
This task is a preservation and regression gate, not a migration.

Continuous collision queries cover endpoint bounds plus optional long travel
corridor. Enclosing that union in one bounding circle is intentionally rejected:
large empty regions would survive BVH node tests and erase expected pruning.

Preserve cell-range computation, maximum-target-radius expansion, hash candidate
enumeration, faction/contact/AABB filters, endpoint and corridor exact tests,
bounded TOI candidates, distance/index sort, consequences, and lifecycle.

## Acceptance Criteria

- Continuous collision reads retained spatial-hash cells and
  `MaxTargetRadius` (or mechanically renamed continuous-only equivalents).
- No `BvhTree`, `BvhTraversalWorkspace`, BVH query circle, or BVH consumer handle
  is added to continuous collision job.
- Endpoint plus travel-corridor union bounds and cell-range calculation remain
  unchanged.
- Existing `MaxContinuousHitsPerFrame`, TOI/index ordering, impact positions,
  contact gates, spawn payloads, and deactivation behavior remain unchanged.
- `ProjectileContinuousSimulationTests` remains spatial-hash regression
  coverage, including long travel corridors and targets near corridor edges.
- Static diff review proves task 003 did not migrate or rewrite continuous
  broadphase.

## Dependencies

- 002 - Target Broadphase Owner And Build retains continuous hash containers.
- 003 - Discrete Projectile BVH Queries.

## Scope / Complexity

Low. Preservation gate preventing scope creep into unsuitable BVH query shape.

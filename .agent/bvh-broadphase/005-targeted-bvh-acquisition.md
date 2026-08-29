# 005 - Targeted BVH Acquisition

## Change

Replace `TargetedAcquisition.Snapshot.OccupiedCells` with read-only selected BVH
view/root. `TrySelectNthNearest` traverses authored search circle, applies current
faction/exclusion/AABB/exact tests, and keeps existing bounded nearest candidate
list with distance then snapshot-index tie break.

Run targeted resolve as `IJobChunk`; reuse one traversal workspace and nearest
candidate list across entities/links in each chunk. Synchronous external
acquisition uses its own local workspace.

Update `TargetedResolveSystem` and `ExternalSpawnGateSystem` to construct BVH
snapshot views. Resolve jobs publish `ConsumerHandle`; synchronous external root
acquisition completes `BuildHandle` before reading and finishes before return.

## Acceptance Criteria

- Targeted acquisition has no AOE-cell dependency.
- Nearest, fork rank, falloff, previous-target exclusion, chain cap, link timing,
  hit/VFX emission, and walk-end lifecycle remain unchanged.
- Large search radius traverses BVH without cell-range loops or radius cap.
- Empty tree and no-hostile cases return false without stale target data.
- External targeted spawn still acquires anchor only after mana succeeds and
  keeps `HasAcquiredTarget` semantics.
- `TargetedResolveEditModeTests` direct snapshot construction and integration
  setup use new broadphase contract; nearest/tie/multi-level regressions added.
- Relevant `TargetedSkillPlayModeTests` remain behavioral integration coverage.

## Dependencies

- 001 - Safe Wide BVH Core.
- 002 - Target Broadphase Owner And Build.

## Scope / Complexity

Medium. Shared acquisition API plus job and synchronous main-thread consumers.

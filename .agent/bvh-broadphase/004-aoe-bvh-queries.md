# 004 - AOE BVH Queries

## Change

Migrate impact and lingering AOE jobs plus `AoeCollisionCore` to BVH candidate
enumeration. Use explicit `IJobChunk` loops with one traversal workspace per
chunk, reset for each enabled AOE.

Build conservative source circle from AOE world bounds. Keep faction, AABB,
exact shape, cooldown, consequence emission, VFX, and impact deactivation logic.
Remove occupied-cell traversal and `FixedList512Bytes<int> seen`; each target
exists as one BVH leaf.

Keep `CollisionConstants.MaxAoeTargetsPerTick`. DFS result order is deterministic
construction order but not gameplay contract. Replace cell-scan-specific overflow
assertion with cap/uniqueness assertions.

## Acceptance Criteria

- Impact and lingering jobs read selected BVH tree and publish consumer handles.
- Parallel chunks never share traversal scratch; entities in one chunk reuse its
  workspace sequentially.
- A target overlapping former multiple cells hits once without query-side de-dup.
- Rotated rectangle/capsule/circle boundary targets are never falsely pruned.
- Faction, pulse cooldown, hit cap, hit/spawn/VFX payloads, and death behavior
  remain unchanged.
- `AoeSimulationTests.PulseOverflowHitsFirstTargetsInCellScanOrder` is replaced
  by behavior-level cap/uniqueness coverage.
- Existing `AoeSimulationTests` and relevant `AoePlayModeTests` cover BVH path.

## Dependencies

- 001 - Safe Wide BVH Core.
- 002 - Target Broadphase Owner And Build.

## Scope / Complexity

Medium. Shared AOE core signature/data path changes; duplicate suppression removed.

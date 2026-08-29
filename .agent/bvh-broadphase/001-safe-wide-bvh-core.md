# 001 - Safe Wide BVH Core

## Change

Add Burst-compatible collision BVH primitives under
`Assets/Scripts/System/Api/Collision/Broadphase/`:

- `BvhConfig.ChildCount` (`4` or `8`);
- circle, child-kind, Morton-entry, build-level records;
- separate BVH4/BVH8 hot-bound and cold-metadata nodes;
- width-selected tree view and resettable safe traversal workspace;
- Morton normalization/interleave, bottom-up builder, parent-circle builder;
- conservative query-circle utility;
- containment, active-mask, root, node-count, and traversal-stack validation.

Use `float4` for BVH4. Use `v256` for BVH8 AVX arithmetic and two `float4`
halves as portable fallback. Do not use pointers, fixed buffers,
`NativeList` per query, or `allowUnsafeCode`.

## Acceptance Criteria

- Changing only `BvhConfig.ChildCount` between 4 and 8 selects matching physical
  node size and query/build path after recompilation.
- Unsupported width fails clearly during owner system creation.
- Empty, partial-root, multi-level, equal-position, negative-coordinate, and
  zero-extent scenes build deterministically.
- All unused lanes remain masked and carry `Unused` kind.
- Every parent child-circle contains referenced object/node circle within chosen
  epsilon.
- Fixed stack capacity is proven sufficient for built tree before publication.
- Workspace reset changes logical count/query state only; it does not clear or
  copy all 512 bytes between entities.
- Randomized circle queries return every brute-force exact overlap; false
  positives allowed.
- Rectangle/capsule object and source bounding circles cannot prune exact hits.
- New EditMode class `BvhBroadphaseEditModeTests` covers above contracts without
  asserting private memory implementation beyond configured width and public
  diagnostics.
- `PlayGround.Sim.asmdef` and project `allowUnsafeCode` remain false.

## Dependencies

None.

## Scope / Complexity

High. New low-level data structure, two SIMD physical layouts, deterministic
builder, traversal, and pure correctness suite.

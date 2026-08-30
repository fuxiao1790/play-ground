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

Use `float4` for BVH4. Its full node overlap equation -- subtract, square,
sum, radius square, compare, and mask extraction -- executes as one packed
four-lane operation, ending in `math.bitmask` or equivalent.

Use two complete `float4` equations for BVH8 and combine their two four-bit
masks. Do not use x86 AVX/AVX2 intrinsics or ISA checks; Burst chooses supported
instructions for target CPU. Scalar lane extraction, scalar arithmetic,
per-lane comparison loops, and per-lane `if` mask construction inside node
geometry are forbidden. Do not rely on Burst auto-vectorization. Do not use
pointers, fixed buffers, `NativeList` per query, or `allowUnsafeCode`.

This BVH is purpose-built for discrete projectile broadphase. Task 001 adds
generic tree primitives but does not authorize continuous projectile, AOE,
targeted, or tracking migration.

## Acceptance Criteria

- Changing only `BvhConfig.ChildCount` between 4 and 8 selects matching physical
  node size and query/build path after recompilation.
- BVH4 source expresses complete overlap math as packed `float4` operations and
  obtains all four results through one packed mask extraction; no scalar
  per-lane comparison/branch occurs before mask production.
- BVH8 source performs two complete packed `float4` tests and combines their
  masks; it does not loop over eight scalar lanes.
- Traversal remains scalar only after node kernel returns lane mask: set-bit
  extraction, child metadata lookup, stack push, and leaf yield.
- Unsupported width fails clearly during owner system creation.
- Empty, partial-root, multi-level, equal-position, negative-coordinate, and
  zero-extent scenes build deterministically.
- All unused lanes remain masked and carry `Unused` kind.
- Every parent child-circle contains referenced object/node circle within chosen
  epsilon.
- Fixed stack capacity is proven sufficient for built tree before publication.
- Workspace reset changes logical stack/mask/query state only; it does not clear
  or copy all 512 bytes between entities.
- Traversal streams leaves through `TryMoveNext`; no fixed candidate collection
  can overflow when more than 127 objects overlap one query.
- Core API supports discrete projectile source-bound circles. Acceptance does
  not require elongated continuous sweep-box queries or any other consumer.
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

High. New low-level data structure, two explicit full-width SIMD kernels,
deterministic builder, scalar mask-driven traversal, and correctness suite.

# 007 - Documentation And Discrete Hash Cleanup

## Change

After correctness/performance evidence, remove only discrete-projectile
dependencies on collision spatial hash and update stale names/docs.

Retain spatial-hash implementation required by continuous projectile collision,
AOE, targeted acquisition, and tracking. Narrowly rename shared fields/constants
only when needed to make retained ownership explicit, for example continuous
projectile collision cells versus AOE occupied cells. Such renames are
mechanical; behavior must not change.

Update authoritative/reference docs to describe target snapshot feeding:

- discrete projectile BVH;
- continuous projectile spatial hash with maximum-target-radius expansion;
- AOE occupied cells reused by targeted chain/external anchor acquisition;
- tracking grid and tracked-target ID map used by projectile tracking
  acquisition.

Document why long continuous sweep boxes remain hash queries. Document safe SIMD
layout, compile-time width change, complete BVH4 packed compare/bitmask pipeline,
complete BVH8 AVX compare/movemask pipeline, two-half packed fallback, scalar
post-mask traversal, ownership/handles, full-rebuild policy,
deterministic-but-unspecified DFS order, validation procedure, Burst codegen
evidence, and discrete benchmark result.

## Acceptance Criteria

- `ProjectileDiscreteCollisionSystem` contains no spatial-hash cells, cell-size,
  `MaxTargetRadius`, or `CombatSpatialHash` query dependency.
- `ProjectileContinuousCollisionSystem` still uses retained spatial-hash cells,
  maximum-target-radius expansion, and cell-range traversal.
- Impact/lingering AOE and targeted acquisition still use retained AOE occupied
  cells with unchanged cap/de-dup/ranking behavior.
- Projectile tracking acquisition still uses tracking grid and ID map unchanged.
- `rg` finds no docs claiming BVH replaces every collision broadphase.
- Docs explicitly state discrete-only BVH scope and long-sweep rationale.
- Folder/system docs describe mixed broadphase owner and retained hash files.
- Docs never describe partial SIMD arithmetic followed by scalar lane overlap
  tests as SIMD lookup; discrete node geometry stays packed through mask
  extraction.
- TODO entry is resolved or rewritten as measured discrete follow-up.
- No compatibility shim, runtime selector, or dual discrete hash/BVH query path
  remains.

## Dependencies

- 001 through 006, including reviewed test XML and benchmark captures.

## Scope / Complexity

Medium. Discrete-only cleanup plus repository-wide mixed-broadphase contract
synchronization.

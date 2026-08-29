# 007 - Documentation And Collision Hash Cleanup

## Change

After correctness/performance evidence, remove collision-hash implementation and
stale names:

- delete projectile collision/AOE occupied map fields and build jobs;
- delete `MaxTargetRadius` and collision-only cell constants/helpers;
- retain a narrowly named tracking-grid helper;
- update all system attributes, test-world registration, comments, and filenames;
- update authoritative and reference docs mentioning shared collision spatial
  hash: target proxy contract, runtime/target lifecycle flows, ECS layer, folder
  structure, performance, projectile/AOE/targeted references, skill reference,
  coding-standard example, and TODO.

Document safe SIMD layout, compile-time width change, fallback behavior,
ownership/handles, full-rebuild policy, deterministic-but-unspecified DFS order,
validation procedure, and benchmark result.

## Acceptance Criteria

- `rg` finds no stale `TargetSpatialHashSystem`, `TargetSpatialHashSingleton`,
  `ProjectileCollisionCells`, `AoeOccupiedCells`, `MaxTargetRadius`, collision
  cell-size, or collision-path `CombatSpatialHash` reference.
- Remaining grid terminology is explicitly tracking-only.
- Docs agree that target snapshots feed collision BVH and tracking grid.
- Docs preserve proxy ordering/lifetime and collision-to-result contracts.
- Folder index points to new broadphase files and systems.
- TODO collision-hash replacement entry is resolved or rewritten as measured
  follow-up.
- No compatibility shim or parallel old/new collision path remains.

## Dependencies

- 001 through 006, including reviewed test XML and benchmark captures.

## Scope / Complexity

Medium. Repository-wide removal and contract/document synchronization.


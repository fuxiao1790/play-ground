# Plan: Eliminate Per-Entity Malloc in Collision Burst Jobs

Index for the implementation of the design in
[collision-broadphase-malloc.md](collision-broadphase-malloc.md).

## Goal

Remove the per-entity `UnsafeUtility.Malloc` traced inside
`AoeCollisionSystem:AoeCollisionJob (Burst)` (primary) and the gate-buffer growth
malloc in both collision jobs. Achieve it with **Route A**: bounded hits per
entity, inline narrow phase, a **local** counter, de-dup via the existing
contact-gate buffer. No per-entity container, no shared per-thread buffer.

## Fixed decisions (no ambiguity)

1. **Approach = Route A.** Delete the AOE `NativeHashSet`. Walk broadphase cells
   inline, run narrow phase per candidate, de-dup against the contact-gate
   buffer, stop at the hit cap. Mirrors the existing `ProjectileCollisionJob`,
   which already has no candidate container.

2. **AOE cap = hard-coded constant `CollisionConstants.MaxAoeTargetsPerTick = 32`,
   per Execute / per tick.** No per-skill component field, no authoring, no config
   entity. 32 is sufficient (far above any real AOE overlap). The local counter
   resets every frame.

3. **Projectile cap = existing `PierceRemaining`.** A projectile **still hits at
   `PierceRemaining == 0`** and despawns once it drops to `-1`: hit → decrement →
   despawn when `< 0`. Authored `PierceCount = N` therefore lands `N + 1` hits.

4. **Gate buffer capacities (kills the secondary malloc).**
   - `AoeContactGateElement`: `[InternalBufferCapacity(CollisionConstants.MaxAoeTargetsPerTick)]` (= 32).
   - `ProjectileContactGateElement`: `[InternalBufferCapacity(16)]` (explicit;
     pierce counts are authored small; rare high-pierce overflow is an accepted
     one-time warm-up cost, documented in code).

5. **Accepted inaccuracy.** When overlap exceeds 32, survivors are decided by
   cell-scan order (`cy → cx → bucket`), i.e. first-N scanned, not nearest-N.
   The cap never realistically fires. Document at the cap site and the break.

6. **Out of scope.** Per-frame writer malloc (`NativeStream` TempJob,
   `NativeQueue` block growth). Deferred (see design doc).

## Tasks

| # | File | Scope | Required for malloc fix |
|---|---|---|---|
| 01 | [001-constants-and-gate-capacity.md](001-constants-and-gate-capacity.md) | Hard-coded constant + gate `[InternalBufferCapacity]` | Yes (secondary) |
| 02 | [002-aoe-collision-rewrite.md](002-aoe-collision-rewrite.md) | Delete `NativeHashSet`, bounded inline AOE collision | **Yes (primary)** |
| 03 | [003-projectile-collision-semantics.md](003-projectile-collision-semantics.md) | Pierce hits at 0, despawns at -1 | No (semantics) |
| 04 | [004-tests-and-verification.md](004-tests-and-verification.md) | Tests + profiler re-capture | Yes (gate) |

## Execution order

`01 → 02` lands the fix (verify with Task 04's profiler step here). `03` aligns
projectile semantics. `04` tests run after 02 and 03.

## Acceptance (whole plan)

- `UnsafeUtility.Malloc` no longer appears as a child of
  `AoeCollisionSystem:AoeCollisionJob (Burst)` in a fresh profiler capture under
  the same scenario as `ProfilerCaptures/play-ground_2026-06-18_18-19-54.csv`.
- Projectile job malloc calls/frame do not increase; gate-growth contributions
  drop to zero within `[InternalBufferCapacity]`.
- AOE damage/hit behavior unchanged for any AOE whose real overlap ≤ 32 (the
  common case). No double-hits (gate dedup preserved).
- All existing collision tests pass; new bounded-cap and pierce tests pass.

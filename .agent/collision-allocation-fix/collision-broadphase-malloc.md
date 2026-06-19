# Collision Broadphase: Per-Entity Malloc Fix (Design)

> Status: design agreed, not yet implemented. Analysis-only so far.
> Affects: `ProjectileCollisionSystem`, `AoeCollisionSystem`.

## Problem

Profiling (`ProfilerCaptures/play-ground_2026-06-18_18-19-54.csv`) shows
`UnsafeUtility.Malloc` as a child node of both Burst collision jobs — native
malloc happening inside the jobs, nearly every frame.

| Job | Frames w/ malloc | Avg calls/frame | Max calls/frame | Malloc SelfMs max |
|---|---|---|---|---|
| `AoeCollisionSystem:AoeCollisionJob (Burst)` | 75 / 93 | 9.4 | 65 | 0.93 |
| `ProjectileCollisionSystem:ProjectileCollisionJob (Burst)` | 91 / 93 | 4.3 | 14 | 0.01 |

AOE malloc scales with scene density (frame 0: 20 calls → frame 91: 65). It is
the worse offender.

## Root causes

1. **AOE per-entity broadphase container (primary).**
   `new NativeHashSet<int>(4, Allocator.Temp)` is constructed and disposed inside
   `Execute`, i.e. once per AOE entity per frame, to de-dup the broadphase
   candidate set before narrow phase. This is the only truly *per-iteration*
   allocation. Projectile avoids it by iterating the multimap inline.

2. **Contact-gate `DynamicBuffer.Add` growth (secondary, both jobs).**
   `ProjectileContactGateElement` / `AoeContactGateElement` are 8-byte elements
   with no `[InternalBufferCapacity]`, so they get the default in-chunk capacity
   of **16**. The 17th distinct target added to one entity's gate buffer moves it
   to a heap allocation. Mostly warm-up cost.

3. **Per-frame writer churn (both jobs, separate concern).**
   `NativeStream` (`VfxPending`) is `Allocator.TempJob`, allocated fresh each
   frame; `NativeQueue` parallel-writer enqueues grow their block pool on spikes.
   These are per-frame, not per-iteration. Out of scope for this change; revisit
   by making the stream persistent if it still shows.

## Design (Route A: bounded hits, no shared writable state)

Cap the number of hits per entity, then process broadphase candidates inline
(projectile pattern) with a local counter. No per-entity container, no
per-thread shared buffer.

### Hit cap source
- **Projectile:** the cap is the existing `PierceRemaining`. A projectile still
  hits at `PierceRemaining == 0`; it despawns once `PierceRemaining` reaches
  `-1` (i.e. hit, then decrement, then despawn when below zero). So authored
  `PierceCount = N` lands `N + 1` hits. A projectile overlapping multiple targets
  emits a hit per target while pierce allows.
- **AOE:** a single **hard-coded** constant cap,
  `CollisionConstants.MaxAoeTargetsPerTick = 32`, applied per Execute / per tick.
  No per-skill component field, no config entity. 32 is sufficient — it is far
  above any real AOE overlap count, so the clamp effectively never fires. The cap
  exists only to bound the broadphase candidate work the `NativeHashSet` absorbed.

Both caps assume the contact gate allows the hit.

### Control flow (both jobs)
1. `remaining = cap` (local int — never a shared per-thread slot).
2. Walk the cells overlapping the entity bounds (existing broadphase).
3. For each candidate target index:
   - If already gated (`IndexOfGate` against the contact-gate buffer) → skip.
     The gate buffer doubles as the per-hit de-dup structure; a duplicate
     candidate that already hit is skipped here.
   - Else run narrow phase (`CombatCollisionMath.Hit`). On hit: emit
     damage/spawn/VFX, add gate, `remaining--`.
   - When `remaining == 0` → break all loops; despawn if appropriate.

The math (`CombatCollisionMath`) is identical for both systems and is
allocation-free.

### Why this removes the malloc
- The `NativeHashSet` is deleted outright (root cause 1 gone).
- `AoeContactGateElement` gets `[InternalBufferCapacity(32)]` (= the cap), so the
  AOE gate buffer never exceeds its in-chunk capacity within a tick → root cause 2
  gone for AOE. `ProjectileContactGateElement` stays at `[InternalBufferCapacity(16)]`;
  rare high-pierce overflow is an accepted one-time warm-up cost.
- No shared writable state across threads → **false sharing is structurally
  impossible** (see below).

## False sharing

Route A keeps the running count as a **local variable**, so there is no
`int`-per-thread accumulator array — the textbook false-sharing trap — and no
shared scratch buffer. Nothing is shared, nothing to false-share.

If Route A is ever swapped for a persistent per-thread scratch buffer (only
needed if a config cap is too large for a stack-local `FixedListN<int>`):
- Pad each thread's stride up to a multiple of `JobsUtility.CacheLineSize` (64B);
  index `threadIndex * paddedStride`, not `threadIndex * cap`.
- Size to `JobsUtility.ThreadIndexCount`.
- Mark the field `[NativeDisableContainerSafetyRestriction]` (disjoint parallel
  writes into one container).
- Keep the count local regardless.

## Accepted inaccuracy (must be documented in code)

When overlap count exceeds the cap (32), the survivors are decided by
cell-iteration order (cy → cx → multimap bucket order), i.e. "first-N scanned,"
not nearest-N. This is **accepted**: 32 is far above any AOE skill's real target
count, so the clamp effectively never triggers in play. Document the clamp at the
cap site and the `Execute` break.

## Config

None. The AOE cap is the hard-coded constant `CollisionConstants.MaxAoeTargetsPerTick`
(also drives `AoeContactGateElement`'s `[InternalBufferCapacity]`). No config
entity, no serialized fields, no per-skill `MaxTargets`. The earlier config-entity
discussion is dropped: Route A has no buffer to size, and `[InternalBufferCapacity]`
must be compile-time anyway.

## Deferred / out of scope
- Per-frame writer malloc (`NativeStream` TempJob, `NativeQueue` block growth) —
  make the stream persistent if it still shows after this change.
- Nearest-N target selection (would reintroduce a bounded sort) — not needed.

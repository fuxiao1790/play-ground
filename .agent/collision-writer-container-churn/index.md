# Plan: Eliminate Per-Frame NativeQueue Block Malloc in Collision Output Writers

Picks up the deferred **item 6** of
[collision-allocation-fix/index.md](../collision-allocation-fix/index.md)
("Per-frame writer malloc — `NativeStream` TempJob, `NativeQueue` block growth").
Independent of the archetype split in
[aoe-gate-lingering-only/index.md](../aoe-gate-lingering-only/index.md) — different
mechanism, different files, can land in either order.

## Symptom (profiled)

`UnsafeUtility.Malloc` traced under `AoeCollisionSystem:AoeCollisionJob (Burst)`
worker threads:

- ~**1354 instances over 8 threads in one frame**, ~**0.81 ms** total.
- **Size: 16384** (16 KB) per allocation. **Allocator: 4 = `Persistent`.**
- Recurs **every frame** — does not taper to steady state.

## Root cause

The 16 KB / `Persistent` blocks are `NativeQueue`'s internal block size. `NativeQueue`
draws blocks from a process-global pool that **retains only a capped number of free
blocks** (~256 → ~4 MB). On block-free the logic is effectively:

```
if (pool.FreeCount < maxRetained)  return block to pool;        // reused next frame
else                               UnsafeUtility.Free(block);   // returned to OS
```

Per-frame peak demand (1354 blocks) is far above the cap, so every frame the surplus
is `Malloc`'d on write and `Free`'d on drain — it never converges, by design.
`ParallelWriter` amplifies it: each of the 8 worker threads holds its own current
block. The queues *are* reused (created once, `Clear`'d per frame); the churn is in
the shared block pool, which reuse cannot fix.

This is **not** the contact-gate buffer and **not** the archetype split — those are
in-chunk footprint, not per-frame `Malloc`.

## Affected containers (all persistent `NativeQueue`, hot)

| Container | Decl | Written by | Drained by |
|---|---|---|---|
| `DamageQueue` | [DamageDispatchBridge.cs:47](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L47) | `AoeCollisionJob.DamageWriter`, projectile collision | `FinalizeDamageQueue` ([:60](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L60)) |
| `EventQueue` | [ProjectileSpawnExpansionSystem.cs:41](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L41) | `AoeCollisionJob.ProjectileEventWriter`, `ProjectileCollisionSystem`, `TimedProjectileSpawnSystem` | `OnUpdate` `TryDequeue` ([:85](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L85)) |
| `BasicProjectileCommandContainer` / `ChildSpawnerProjectileCommandContainer` | [:42-43](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L42) | `ProjectileExpansionJob` | apply systems |

## Fix: pre-sized, reused `NativeList<T>`

`NativeList.Clear()` sets `Length = 0` but **keeps `Capacity`**, so capacity ratchets
to the real per-frame peak over the first few frames and then **stops allocating** —
exactly the "allocate to grow for a few frames, then settle" behavior `NativeQueue`
denies.

Per container:

- **Producers:** `list.AsParallelWriter().AddNoResize(...)` instead of `queue.Enqueue(...)`.
- **Consumers:** read `list.AsArray()` directly instead of the `TryDequeue` loop.
- **Reset:** `list.Clear()` at the start of the frame (before producers run).
- **Dependency:** combine producer `ParallelWriter` job handles into the existing
  `ProducerHandle` exactly as the queues do today.

### Bonus allocations removed (forced by the queue design)

- `FinalizedDamageEvents = new NativeArray<>(damageCount, Persistent)` **every frame**
  — [DamageDispatchBridge.cs:73](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L73).
  Gone: replay reads the list's `AsArray()` directly.
- `events = new NativeArray<>(totalEvents, TempJob)` **every frame**
  — [ProjectileSpawnExpansionSystem.cs:82](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L82).
  Gone: expand directly over the event list.

## Open questions

- **Capacity policy (`AddNoResize` cannot grow inside a job).** Options: (a) pre-size
  each list to a worst-case constant; (b) main-thread auto-ratchet — grow `Capacity`
  when the prior frame's peak count approached it, settling after warmup
  (**recommended**, self-tunes). Decide overflow handling on the ratcheting frame:
  size with margin, or detect the clamp and drop the overflow that one frame.
- **Scope this pass.** Damage path only first (`DamageQueue` + its producers), or all
  four containers in one change? Suggest damage path first to validate the profiler
  flatlines, then the projectile `EventQueue` + command containers.
- **`vfxPending` NativeStream (secondary).** [AoeCollisionSystem.cs:89](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L89)
  is a fresh `TempJob` `NativeStream` per frame (Allocator 3, not the 16 KB Persistent
  churn). Lower priority; fold in only if it shows up after the queues are fixed.

## Acceptance

- No per-frame `UnsafeUtility.Malloc` of 16384-byte `Persistent` blocks under
  `AoeCollisionJob` / `ProjectileCollisionJob` at steady state; allocations taper to
  ~0 within a few seconds of warmup (re-capture under the same scenario as the
  profile that showed 1354 instances / 0.81 ms).
- `DamageDispatchBridge` no longer allocates `FinalizedDamageEvents` per frame;
  `ProjectileSpawnExpansionSystem` no longer allocates the `events` array per frame.
- Damage replay grouping/crit and projectile spawn fan-out behavior unchanged; all
  collision, projectile, and AOE simulation tests pass.

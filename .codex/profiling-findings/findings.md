# Profiling Findings — 2026-06-03

**Capture:** `ProfilerCaptures/play-ground_2026-06-03_20-22-22.csv`
**Condition:** Editor play mode, lingering AOEs active (stress benchmark)
**Observed:** ~120–150ms frames (6–8 fps). Rendering at ~48% of frame time.
**Baseline:** Rendering normally 15–35% in this benchmark. Lingering AOEs pushed it above that band — the CPU costs (DrainEvents, collision jobs) are expected stress-test load; the rendering spike is the regression.

---

## Root Causes

### 1. GPU fill-rate ceiling — per-instance transparent overdraw (rendering regression)

**Marker:** `GfxDeviceD3D12.WaitForLastPresentation.WaitForGPU`
**Cost:** 13,158ms total · avg 20.7ms/frame · ~48% of frame time (normal: 15–35%)

CPU is blocked waiting for GPU to present previous frame.

**Why lingering AOEs specifically push rendering above the normal band:**
- Normal runs: projectiles leave the screen quickly → transparent instance count stays bounded.
- Lingering AOEs (`BasicStarAoeConfig`: 5s lifetime) accumulate to the 10,000 cap at steady state.
- All 10,000 instances cover the entire play area by design. No culling is possible or relevant.
- All are in the transparent queue (`RenderQueue.Transparent + 45`) → no early-Z rejection. The GPU fragments every overlapping instance for every covered pixel, back-to-front.
- At 1023 instances per `DrawMeshInstanced` call: ~10 draw calls, each a full-play-area transparent pass. GPU fill rate is the ceiling.
- Projectile CPU submission ~0.11ms/frame vs AOE ~0.57ms/frame — confirms AOE instances drive GPU load.

**The per-instance transparent rendering model hits a hard GPU fill-rate limit at this density.** There is no tuning fix within the current approach. Reducing rendering cost at high lingering-AOE density requires a different strategy, such as:
- Accumulate AOE visuals into a shared render texture (single additive pass regardless of instance count).
- Cap the number of rendered AOE instances (visuals only; collision runs for all) with a render budget.

---

### 2. `AoeRoot.DrainEvents` — hit callback volume

**Cost:** 7,534ms total · avg 14.2ms/call · runs in `LateUpdate`
**File:** `Assets/Scripts/System/Aoe/AoeRoot.cs:99`

`BasicStarAoeConfig.tickIntervalSeconds = 0.15` → each AOE fires ~6–7 hits/second. With many lingering AOEs × 20 targets, the hit buffer contains thousands of entries per frame. Per hit:
```csharp
new DamageSnapshot(...)       // GC alloc
new AoeHitContext(...)        // GC alloc
AoeHit?.Invoke(context)       // managed delegate
target?.ReceiveAoeHit(damage) // vtable → MobRoot
```

GC confirmed: `GarbageCollector.CollectIncremental` 63ms + `GC.Collect` 47ms across capture.

**Fix (pick one or combine):**
- Eliminate per-hit heap allocations: `DamageSnapshot` / `AoeHitContext` should be `readonly struct` passed by-value or ref.
- Cap hit events processed per `DrainEvents` call; spread remainder to next frame.
- Raise `BasicStarAoeConfig.tickIntervalSeconds` if tick rate exceeds gameplay requirements.

---

### 3. MobRoot.FixedUpdate — hit callback side-effects

**Cost:** 1,426ms total · avg 2.8ms/call
**Cause:** `ReceiveAoeHit` on mobs driving health/state/animation logic. Downstream of issue #2; fixing hit volume fixes this.

---

### 4. AOE collision Burst jobs (informational — expected benchmark load)

| Job | Total | Avg |
|-----|-------|-----|
| `AoeCollisionJob` | 732ms / 550 calls | 1.33ms |
| `AoeHitFlushJob` | 635ms / 355 calls | 1.79ms |
| `AoeContactGateJob` | 88ms / 284 calls | 0.31ms |

Burst-compiled. High call count expected at this AOE density.

---

### 5. `ActiveAoeCount()` — O(N) per-entity query risk

**File:** `Assets/Scripts/System/Aoe/AoeRoot.cs:427`

Iterates all AOE entities individually via `ToEntityArray` + per-entity `GetComponentData`. Called from the `Counters` property. Not confirmed to be on the hot path in this capture, but will be expensive if `Counters` is read each frame at high AOE counts. `PerformanceText` already uses the cheaper `CalculateEntityCount()` pattern — `ActiveAoeCount` should match.

---

## Cost Summary

| Marker | Total ms | Avg/call | Category |
|--------|----------|----------|----------|
| `GfxDeviceD3D12.WaitForLastPresentation.WaitForGPU` | 13,158 | 20.7ms | GPU fill-rate ceiling |
| `AoeRoot.DrainEvents` | 7,534 | 14.2ms | Managed hit volume |
| `MobRoot.FixedUpdate` | 1,426 | 2.8ms | Hit callback side-effects |
| `AoeCollisionJob (Burst)` | 732 | 1.33ms | Expected |
| `AoeHitFlushJob (Burst)` | 635 | 1.79ms | Expected |
| `AoeRoot.SubmitAoes` | 345 | 0.57ms | CPU draw submission |
| GC (incremental + full) | ~110 | — | Per-hit allocs in DrainEvents |

---

## Action Items (priority order)

1. Decide rendering strategy for high-density lingering AOEs: accumulation render texture OR per-instance visual cap (collision unaffected either way)
2. Eliminate per-hit `DamageSnapshot` / `AoeHitContext` heap allocations in `AoeRoot.DrainEvents`
3. Cap or throttle hit events in `DrainEvents` if tick rate × AOE count × target count exceeds budget
4. Confirm `AoeRoot.Counters` is not read per-frame; if so, replace `ActiveAoeCount()` with chunk-level query
5. Re-profile in-player (Editor overhead is significant — `EditorLoop` consumed 63–88% of total in some frames)

# 001 — CombatHitEvent + unbounded queue + count-sized bucket + combined push (inert)

**Change:** add · **Depends:** — · **Scope:** medium

## Goal

Introduce the unified hit event, the unbounded production queue, the
count-sized bucket map, and the two systems that replace the damage path — wiring
nothing into the queue yet, so behavior is unchanged (green-but-inert). The push
to the GameObject is a **single combined call** (`ReceiveCombat`) from the start.

## Changes

1. **`CombatHitEvent`** (new struct, in
   [CombatHitElement.cs](../../Assets/Scripts/System/Common/CombatHitElement.cs)
   next to `DamageReplayEvent`). Carries both halves so producers emit once:
   - damage: `TargetProxy, Kind, DamageAmount, CritChance, CritMultiplier,
     DirectDamageEnabled, HitPosition, SourceNodeId, SourceId, TypeId`
   - status: `StackEffectSnapshot StackEffect`
   Stack field unused until 003 but defined now to fix the wire format.

2. **`ReceiveCombat`** on `ICombatTarget`
   ([ICombatTarget.cs:55-64](../../Assets/Scripts/System/Common/ICombatTarget.cs#L55)) —
   the combined push, replacing the role of `ReceiveHits` as the dispatch entry:
   ```csharp
   void ReceiveCombat(IReadOnlyList<CombatHitData> hits,
                      IReadOnlyList<StatusStackSnapshot> stacks)
   {
       for (int i = 0; i < hits.Count; i++) { var h = hits[i]; ReceiveHit(in h); }
       // status: default no-op; overridden by targets that care (004)
   }
   ```
   with `StatusStackSnapshot { int DebuffKey; int Count; float LifetimeRemaining; }`.
   Keep `ReceiveHit`; `ReceiveHits` may stay as a helper but the bridge calls
   `ReceiveCombat`.

3. **`CombatApplyFinalizeSystem`** (`SimulationSystemGroup`,
   `[UpdateAfter]` the three collision systems, `[UpdateBefore]` the two
   spawn-expansion systems). Owns production + bucketing + finalize:
   - Owns **unbounded** `NativeQueue<CombatHitEvent> HitQueue` (Persistent) and
     exposes `.AsParallelWriter()` + a `ProducerHandle` for collision jobs to
     combine into (same pattern as
     [DamageDispatchBridge.cs:40-48](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L40)).
     This is the same primitive as today's `DamageQueue` — **no cap**.
   - `OnUpdate`: complete `ProducerHandle`; `n = HitQueue.Count`; if 0, clear+return.
     1. `flat = HitQueue.ToArray(Temp)` — serial memcpy, **no sort**.
     2. `map = new NativeParallelMultiHashMap<Entity,int>(n, Temp)` — **capacity ==
        actual count**, so it can never overflow regardless of load.
     3. parallel `IJobParallelFor` over `flat`: `map.AsParallelWriter().Add(
        flat[i].TargetProxy, i)`.
     4. `keys = map.GetUniqueKeyArray(Temp)` — the grouping, **no sort**.
     5. parallel job over `keys`: for each hit index in `map.GetValuesForKey(target)`,
        roll crit with `Unity.Mathematics.Random` seeded `math.hash(uint3(
        (uint)target.Index, frameCount, hitIndex))` → rolled `DamageSnapshot`;
        write rolled per-hit results into a frozen `NativeList<RolledHit>` plus a
        `NativeArray<TargetResultRange>` of `(target, hitStart, hitCount,
        statusStart, statusCount)`. Per-hit, **not condensed**.
   - complete the job; hand the frozen arrays to `CombatApplyBridge`.
   - (status arrays are produced empty here; filled in 003.)

4. **`CombatApplyBridge`** (`PresentationSystemGroup`, before the render system).
   Mirrors `DamageDispatchBridge.ReplayDamage` but consumes frozen ranges and
   pushes **once**:
   - for each `TargetResultRange`: resolve `TargetCompanion` → `ICombatTarget`
     ([DamageDispatchBridge.cs:176-192](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L176)),
     build the `CombatHitData` list (crit already rolled — no `RollDamage`) and the
     `StatusStackSnapshot` list from the frozen slices, call `ReceiveCombat`.
   - port the `IsTargetUsable` / dead-proxy guards verbatim.
   - dispose frozen arrays after push (same lifecycle as `FinalizedDamageEvents`).

5. **`RolledHit` / `TargetResultRange`** helper structs (blittable, Burst-friendly).

## Acceptance criteria

- Compiles; the old damage path (`DamageFinalizeSystem`/`DamageDispatchBridge`)
  is untouched — gameplay unchanged.
- `HitQueue`/temp allocations are clean (no leak warnings on teardown).
- With no producers, finalize sees an empty queue and the bridge no-ops.
- No `Sort` call anywhere in the new path; the bucket map is sized to
  `flat.Length`.

## Notes / risks

- `GetValuesForKey` order within a bucket is unspecified; damage is
  order-independent and the crit seed uses a local `hitIndex`, so determinism does
  not depend on enumeration order — document the seed derivation.
- Keep the finalize/bridge as a managed pair so the sim-freeze / presentation-push
  split stays explicit.
- `ToArray` is a flat memcpy — far cheaper than the old per-element dequeue loop;
  it is the only serial step and is not the bottleneck.

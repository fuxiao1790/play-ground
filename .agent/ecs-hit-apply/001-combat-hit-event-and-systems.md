# 001 — CombatHitEvent + bucketed map + apply systems (inert)

**Change:** add · **Depends:** — · **Scope:** medium

## Goal

Introduce the unified hit event, the target-bucketed map, and the two systems
that replace the damage path — but wire nothing into the map yet, so behavior is
unchanged (green-but-inert). This isolates the new infrastructure and the
parallel finalize job from the producer/consumer switch in 002.

## Changes

1. **`CombatHitEvent`** (new struct, in
   [CombatHitElement.cs](../../Assets/Scripts/System/Common/CombatHitElement.cs)
   next to `DamageReplayEvent`). Carries both halves so producers emit once:
   - damage: `Kind, DamageAmount, CritChance, CritMultiplier, DirectDamageEnabled,
     HitPosition, SourceNodeId, SourceId, TypeId`
   - status: `StackEffectSnapshot StackEffect`
   The stack field is unused until 003 but defined now to fix the wire format.

2. **`HitApplyFinalizeSystem`** (`SimulationSystemGroup`,
   `[UpdateAfter(ProjectileCollisionSystem)]`, `[UpdateAfter(ImpactAoeCollisionSystem)]`,
   `[UpdateAfter(LingeringAoeCollisionSystem)]`, `[UpdateBefore(AoeSpawnExpansionSystem)]`,
   `[UpdateBefore(ProjectileSpawnExpansionSystem)]`). Mirrors `DamageFinalizeSystem`'s
   role but does the freeze with a job:
   - Owns `NativeParallelMultiHashMap<Entity, CombatHitEvent> HitMap` (Persistent)
     and exposes `.AsParallelWriter()` + a `ProducerHandle` for collision jobs to
     combine into (same pattern as
     [DamageDispatchBridge.cs:40-48](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L40)).
   - `OnUpdate`: complete `ProducerHandle`; `keys = HitMap.GetUniqueKeyArray(Temp)`
     (this is the grouping — **no sort**); schedule a parallel job over `keys`:
     - for each hit in `HitMap.GetValuesForKey(target)`: roll crit with
       `Unity.Mathematics.Random` seeded `math.hash(uint3((uint)target.Index,
       frameCount, hitIndex))` → rolled `DamageSnapshot(amount, isCrit)`.
     - write rolled per-hit results contiguously into a frozen
       `NativeList<RolledHit>` plus a `NativeArray<TargetHitRange>` of
       `(target, start, count)`. Per-hit, **not condensed** (count preserved).
   - complete the job and hand the frozen arrays to `HitApplyBridge`.
   - **Map sizing:** allocate/ensure capacity from last frame's hit high-water
     mark; if `HitMap.Count()` approached capacity, grow next frame. Guard against
     overflow with a single warning log.

3. **`HitApplyBridge`** (`PresentationSystemGroup`, before the render system).
   Mirrors `DamageDispatchBridge.ReplayDamage` but consumes the frozen ranges
   instead of sorting:
   - for each `TargetHitRange`: resolve `TargetCompanion` → `ICombatTarget`
     ([DamageDispatchBridge.cs:176-192](../../Assets/Scripts/System/Common/DamageDispatchBridge.cs#L176)),
     build a `CombatHitData` list from the rolled slice, call `ReceiveHits`.
   - crit is already rolled in the job, so there is **no** `RollDamage` here.
   - dispose frozen arrays after push (same lifecycle as `FinalizedDamageEvents`).

4. **`RolledHit` / `TargetHitRange`** helper structs (Burst-friendly, blittable).

## Acceptance criteria

- Project compiles; the old damage path (`DamageFinalizeSystem` /
  `DamageDispatchBridge`) is still in place and untouched — gameplay unchanged.
- `HitMap` is allocated/disposed cleanly (no leak warnings on world teardown).
- With no producers, the finalize job sees an empty map and the bridge no-ops.
- The finalize job iterates `GetUniqueKeyArray` with no `Sort` call anywhere.

## Notes / risks

- `GetValuesForKey` enumeration order within a bucket is unspecified; damage is
  per-hit and order-independent, but the crit seed uses a local `hitIndex` so
  determinism does not depend on enumeration order matching across runs — confirm
  the seed derivation is stable (document it).
- Keep `HitApplyFinalizeSystem` + `HitApplyBridge` as a managed pair like the
  existing bridge so the sim-freeze / presentation-push split is explicit.

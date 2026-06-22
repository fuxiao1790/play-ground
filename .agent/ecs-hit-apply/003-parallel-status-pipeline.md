# 003 — Parallel status: accrue in finalize job, process in new system

**Change:** adapt · **Depends:** 002 · **Scope:** large

## Goal

Move stack accrual into the parallel finalize job (writing the ECS accumulator),
and move tick/fizzle/detonation into a new parallel `StatusProcessSystem`. Retire
`StackAccrualSystem` and `StackApplyEvent`. Landed as one task because splitting
accrual from detonation would leave the accumulator with two writers in an
inconsistent intermediate.

## Changes

1. **Carry the stack payload** — collision jobs fill `CombatHitEvent.StackEffect`
   from the hit payload (the value they already pass to `StackApplyEvent` today,
   [ProjectileCollisionSystem.cs:312-321](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L312)).
   Remove the separate `StackApplyEvent` enqueue.

2. **Accrue in the finalize job** (`CombatApplyFinalizeSystem`, 001) — for each hit
   with `StackEffect.Enabled`, fold into that target's
   `DynamicBuffer<TargetStackEntry>` accumulator
   ([CombatTargetProxy.cs:31-42](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L31)):
   increment `Count`, sum `Contribution`, refresh `LifetimeRemaining` — i.e. the
   accrual half of `StackAccrualSystem.Apply`
   ([StackAccrualSystem.cs:164-217](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L164)),
   **minus** the threshold/detonate branch (that moves to step 3). Access via
   `BufferLookup<TargetStackEntry>` + `[NativeDisableParallelForRestriction]`;
   each target is owned by one job index, so no aliasing. Keep the
   `MaxTargetStackEntries`/eviction guard.
   Then **fill the status half of the combined result**: after accrual, write the
   target's current accumulator entries as `StatusStackSnapshot`s into the frozen
   status arrays referenced by `TargetResultRange` (001), but only for targets
   whose stacks changed this frame. This is what `CombatApplyBridge` delivers via
   `ReceiveCombat` — the snapshot is captured **pre-reduction** (before step 3
   consumes on detonation); document that timing.

3. **`StatusProcessSystem`** (new, `SimulationSystemGroup`, after
   `CombatApplyFinalizeSystem`, `[UpdateBefore]` the spawn-expansion systems). A
   parallel job over entities with `TargetStackEntry`:
   - **tick/fizzle** — decay `LifetimeRemaining`, drop expired entries (today's
     `TickAndFizzle`, [StackAccrualSystem.cs:119-142](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L119)).
   - **detonate + reduce** — when `Count >= Threshold`, build the detonation
     spawn and enqueue it into the existing `AoeSpawnExpansionSystem` /
     `ProjectileSpawnExpansionSystem` queues via **ParallelWriter**, then consume
     the entry. Port `BuildDetonationSpawn` / `BuildAoeSpawnEvent` /
     `BuildProjectileDetonation`
     ([StackAccrualSystem.cs:247-365](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L247))
     into Burst-compatible job code. Detonation spawns **same frame** (before
     spawn-expansion); the product collides next frame.
   - `NextAoeId` / `NextProjectileDetonationSourceId` counters
     ([StackAccrualSystem.cs:367-381](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L367))
     become an atomic/`NativeReference` counter or per-thread offset since the job
     is parallel.

4. **Remove** `StackAccrualSystem`
   ([StackAccrualSystem.cs](../../Assets/Scripts/System/Status/StackAccrualSystem.cs))
   and `StackApplyEvent`.

## Acceptance criteria

- Stacks accrue, fizzle, and detonate as before; detonation AOE/projectile
  products spawn the same frame and match the previous geometry/damage scaling
  ([BuildAoeSpawnEvent](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L279) parity).
- No `StackAccrualSystem` / `StackApplyEvent` references remain.
- Accrual and detonation never run in the same frame phase on the same entity
  (finalize job accrues before `StatusProcessSystem` reduces).
- The status half of `ReceiveCombat` now carries the per-target snapshot; the HP
  half is unchanged from 002. Both still cross in one call.
- The `events.Sort` from the old accrual path is gone.

## Notes / risks

- **Detonation semantics with parallel detonate:** decide multi-trigger behavior
  — if a single frame's accrual pushes `Count` well past `Threshold`, either
  detonate once (old behavior) or `Count / Threshold` times (carry remainder).
  Old behavior detonated once per crossing; pick and document. Default: match old
  (single detonation, clear entry).
- Parallel writes to spawn queues are already supported (collision jobs do it);
  forward `StatusProcessSystem`'s job handle into the expansion `ProducerHandle`s
  the same way ([StackAccrualSystem.cs:257-271](../../Assets/Scripts/System/Status/StackAccrualSystem.cs#L257)).
- `StatusProcessSystem`'s job both reads/writes buffers and ticks decay — keep it
  one job over the entity set so each buffer is touched once.

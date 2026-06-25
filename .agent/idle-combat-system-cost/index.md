# Idle Combat System Cost — Implementation Plan

## Problem

After a busy combat scene clears, the frame stays pinned at ~13 ms (~77 fps)
even with nothing drawn on screen. Profiler capture
`ProfilerCaptures/play-ground_2026-06-25_09-57-35.csv` shows several combat
systems still scheduling jobs and allocating `TempJob` containers every frame
regardless of how many combat entities are active.

> **Rendering excluded.** The largest single idle cost in the capture was
> `CombatBatchedRenderSystem` (~4.97 ms/frame producing zero draws), but the
> render path is being reworked separately, so it is out of scope for this plan.
> This plan covers the remaining non-render idle waste and the underlying pool
> growth that keeps cost high after a busy scene.

### Root cause

1. **The projectile/AOE pool only grows.** Despawn disables the `Active` /
   `*CollisionActiveTag` / render enableable components; entities are never
   destroyed during churn (`DestroyEntity` is only used for target-proxy death
   and root teardown). The high-water-mark population of disabled entities stays
   resident in chunks.

2. **Hot systems do per-frame work proportional to the resident pool or the
   target set, not to the active-combat count.** The reuse pipeline packs new
   active entities into recently-disabled chunks, so active/disabled stay
   interleaved and enableable chunk-skipping rarely triggers — every gather and
   `ScheduleParallel` keeps touching the whole pool.

Systems that already guard correctly (leave alone): `ProjectileCollisionSystem`,
`Lingering/ImpactAoeCollisionSystem`, `StatusProcessSystem`,
`CombatApplyFinalizeSystem`, `CombatRenderPrepareSystem`.

Systems that waste idle work (target of this plan): `ProjectileTrackingSystem`,
`CombatLifetimeSystem`, `AoePulseVfxSystem`, `ProjectileMovementSystem`.

## Approach

Two layers:

- **Stop the per-frame waste** — add empty-work guards so idle systems do
  nothing when no entities are active (tasks 001, 002).
- **Stop the scaling** — trim the disabled pool back down after a busy scene so
  chunk count (which drives every query/schedule) falls instead of staying
  pinned at the high-water mark (task 003).

Tasks 001/002 are low-risk quick wins. Task 003 addresses the "after a busy
scene clears up" scaling and is the durable fix; with rendering out of scope it
is the headline of this plan.

### Decisions

- Prefer `state.RequireForUpdate(activeQuery)` (or an explicit
  `CalculateEntityCount() == 0` early-out where a system has multiple roles)
  over scheduling no-op jobs. This mirrors the existing guarded systems.
- Pool trimming is a dedicated periodic main-thread system with hysteresis, not
  an inline per-frame destroy, to keep structural-change sync points rare.
- Rejected alternative: segregating disabled entities into fully-disabled chunks
  to enable chunk-skip. It fights the reuse-into-recently-disabled pooling
  pattern and is superseded by pool trimming (003). Documented here, not
  scheduled.

## Tasks

| # | File | Summary | Impact | Risk |
|---|------|---------|--------|------|
| 001 | [001-simulation-system-early-outs.md](001-simulation-system-early-outs.md) | Guards for lifetime/pulse/movement systems | Medium | Low |
| 002 | [002-tracking-system-guard.md](002-tracking-system-guard.md) | Skip target-hash build when no projectiles active | Medium | Low |
| 003 | [003-idle-pool-trim.md](003-idle-pool-trim.md) | Trim disabled pool after busy scenes | High (durable) | Medium |

Recommended order: 001 → 002 → 003. All three are independent; 001/002 are the
fast guards, 003 is the larger structural change.

## Verification

- Re-capture an idle frame after a busy scene; confirm the four targeted systems
  schedule no jobs and allocate nothing when no entities are active.
- Confirm idle-frame `PlayerLoop` time falls below the current ~13 ms with no
  mobs/combat active (net of the separate render rework).
- Confirm a busy scene still simulates identically (no dropped
  projectiles/AOEs); existing PlayMode combat tests pass.
- After a busy→clear transition, confirm resident disabled projectile/AOE entity
  count falls back toward the reserve floor (task 003).

## Out of scope

- **Rendering** (`CombatBatchedRenderSystem` and related) — being reworked
  separately.
- `MobRoot.Update`/`FixedUpdate` (~2.8 ms combined) — managed scene-object mob
  logic, not pooled ECS entities. Track separately.

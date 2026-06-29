# VFX: remove the single-threaded flush job

## High-level summary

VFX requests currently flow through a redundant single-threaded copy. We remove
the intermediate `DynamicBuffer<VfxSpawnRequestElement>` and the
`VfxFlushJob`/`VfxStreamFlushJob` `IJob`s, and have every simulation producer
write `VfxPendingSpawn` directly into **one persistent
`NativeQueue<VfxPendingSpawn>`** owned by `CombatVfxDispatchSystem`, via
`AsParallelWriter()`. Presentation drains that queue on the main thread (as it
already did with the buffer) and dispatches. This conforms to the project's
existing collector pattern (`CombatApplyFinalizeSystem.HitQueue`), which the same
collision systems already feed the same way.

## Why this change

Today, per frame:

1. Each producer allocates a TempJob `NativeQueue`/`NativeStream`, writes
   `VfxPendingSpawn` into it.
2. A single-threaded burst `IJob` (`VfxFlushJob` / `VfxStreamFlushJob`) copies
   that container into the shared `DynamicBuffer<VfxSpawnRequestElement>` on the
   `VfxSingleton` entity. (`AoeSpawnExpansionSystem` instead writes the buffer
   directly on the main thread.)
3. `CombatVfxDispatchSystem` (presentation) drains that buffer **again on the
   main thread** via `CombatVfxRoot.DrainAndDispatch` → `StageSpawn`, then
   dispatches.

The buffer is a pure intermediate: data is copied native → DynamicBuffer
(single-threaded `IJob`, once per producer) → staging lists (main thread) → GPU,
plus 5 TempJob alloc/dispose pairs per frame. The flush jobs and the buffer add
nothing the main-thread drain doesn't already do.

## Constraints & invariants the change must respect

| Invariant | Source | How the plan honors it |
|---|---|---|
| Simulation jobs must not call managed VFX objects; VFX is visual-only. | `Docs/reference/simulation/vfx-system.md` (Non-goals), `Docs/contracts/vfx-requests.md` (Restrictions) | Producers still only enqueue plain `VfxPendingSpawn`; all managed `VisualEffect`/`GraphicsBuffer` work stays in `CombatVfxDispatcher` on the main thread. |
| The managed dispatch boundary is main-thread. | `CombatVfxDispatcher.Dispatch` touches `VisualEffect`/`GraphicsBuffer` | Unchanged. Presentation completes producers, then drains+dispatches on the main thread. This is *not* the "single-threaded flush" being removed. |
| Multiple producer systems write one shared native container across the frame; reads happen after all writes complete. | `CombatApplyFinalizeSystem.HitQueue` + `ProjectileCollisionSystem`/`ImpactAoeCollisionSystem`/`LingeringAoeCollisionSystem` (`hitApply.AsParallelWriter()` + `ProducerHandle`) | Reuse that exact mechanism: persistent `NativeQueue`, `AsParallelWriter()`, producers combine their scheduled handle into `ProducerHandle`; consumer `.Complete()`s it before the main-thread drain. |
| VFX is visual-only and may be capped/dropped without affecting gameplay. | `Docs/contracts/vfx-requests.md` (Guarantees), `CombatVfxDispatcher.StageSpawn` `maxPerFrame` cap | Preserved — drain still funnels through `StageSpawn`, which caps per `(typeId, trigger)`. |
| `AreaSize <= 0` must be clamped before reaching a graph. | `VfxFlushJob` (`> 0f ? : 1f`) and `CombatVfxDispatcher.StageSpawn` (`math.max(0.01f, areaSize)`) | The flush-job clamp is redundant; `StageSpawn` already clamps. Drain relies on `StageSpawn`. |
| Native containers have explicit owned lifetimes; no leaks. | `CombatApplyFinalizeSystem.OnCreate/OnDestroy` (Persistent alloc + Complete-then-dispose) | The new queue is `Allocator.Persistent`, allocated in `OnCreate`, `ProducerHandle.Complete()` + dispose in `OnDestroy`. |
| VFX dispatch is faction-agnostic and owns a presentation singleton. | `Docs/reference/simulation/vfx-system.md` (Data Flow) | Ownership moves from a `VfxSingleton` ECS entity to the `CombatVfxDispatchSystem` managed singleton system — still one faction-agnostic owner. |

## Mechanisms reused vs. introduced

- **Reused (no new mechanism):** the persistent-`NativeQueue` + `AsParallelWriter()`
  + `ProducerHandle.CombineDependencies` + consumer `.Complete()` pattern from
  `CombatApplyFinalizeSystem`. The collision systems already wire VFX *and*
  `HitQueue` in the same scheduling blocks, so the VFX queue rides alongside an
  identical, proven path.
- **Introduced:** nothing structurally new. We *remove* a data type, a buffer,
  and two jobs. The only addition is fields/methods on `CombatVfxDispatchSystem`
  mirroring `CombatApplyFinalizeSystem`.

## Minimal/additive vs. refactor comparison

**Minimal/additive approach** (keep buffer + types, try to parallelize the flush):
- resulting data flow: producer container → (parallel-ish flush) → DynamicBuffer
  → main-thread drain → GPU. `DynamicBuffer.Add` is not parallel-writable, so a
  "parallel flush" still needs a per-thread list then a serial merge.
- new concepts/types introduced: a merge/staging list per producer; keeps both
  `VfxPendingSpawn` and `VfxSpawnRequestElement`.
- copies/translations added: still ≥2 copies; adds another.
- long-term cost: two data types for one concept persist; two write paths
  (job-flush vs `AoeSpawnExpansionSystem` main-thread) stay out of sync; the
  buffer remains a redundant source of truth.

**Refactor approach (chosen)** — collapse to one queue:
- resulting data flow: producer jobs → `ParallelWriter` → one persistent
  `NativeQueue<VfxPendingSpawn>` → main-thread drain → GPU.
- existing concepts/types changed or removed: delete `VfxSpawnRequestElement`,
  `VfxSingleton`, `VfxFlushJob`, `VfxStreamFlushJob`; `VfxPendingSpawn` becomes
  the single payload type.
- copies/translations removed: removes the native→buffer flush copy and the
  byte↔int `Trigger` translation; removes 5 TempJob alloc/dispose pairs.
- long-term benefit: one payload type, one queue, one owner, one write path;
  fewer jobs; collision jobs lose `NativeStream` Begin/End bookkeeping.

**Decision: choose refactor.**
Reason: `VfxPendingSpawn` and `VfxSpawnRequestElement` are two representations of
the same concept (identical fields, `Trigger` only differs `byte` vs `int`)
bridged by a copy-only job. Per the default decision rule below, that collapses
to one source of truth. The refactor strictly reduces data paths, copies, types,
and jobs, with no compatibility reason to keep the split.

## Default decision rule applied

Two representations describe the same domain concept (a pending VFX request:
`TypeId`, `Trigger`, `Position`, `AreaSize`). Refactor toward one source of truth
(`VfxPendingSpawn`); there is no compatibility/migration reason to keep
`VfxSpawnRequestElement`.

## Design validation against invariants

- *Visual-only / no managed calls in jobs:* producers still enqueue POD only;
  managed work unchanged in `CombatVfxDispatcher`. ✓
- *Cross-system shared-container safety:* identical to `HitQueue`, which 3
  collision systems already feed via `AsParallelWriter()` + `ProducerHandle`
  with the consumer completing the handle before reading. No new safety surface. ✓
- *Lifetime:* Persistent alloc in `OnCreate`, Complete+dispose in `OnDestroy`,
  mirroring `CombatApplyFinalizeSystem`. ✓
- *Cap/clamp behavior:* preserved by routing the drain through `StageSpawn`. ✓
- *Ordering:* owner is a presentation-group system allocated at world init, so
  simulation-group producers can fetch it via `GetExistingSystemManaged`;
  `ProducerHandle.Complete()` runs in presentation after all simulation. ✓

## Tasks

| # | File | Change |
|---|---|---|
| [001](001-collector-system.md) | `CombatVfxDispatchSystem.cs` | Own the persistent `NativeQueue<VfxPendingSpawn>`; expose `AsParallelWriter()`/`ProducerHandle`/`HasQueue`; drain+dispatch from the queue. |
| [002](002-vfx-root-drain.md) | `CombatVfxRoot.cs` | `DrainAndDispatch` consumes the queue instead of a `NativeArray<VfxSpawnRequestElement>`. |
| [003](003-queue-producers.md) | `CombatLifetimeSystem.cs`, `AoePulseVfxSystem.cs` | Drop local queue+flush+dispose; write to the shared collector's parallel writer. |
| [004](004-collision-producers.md) | `ProjectileCollisionSystem.cs`, `ImpactAoeCollisionSystem.cs`, `LingeringAoeCollisionSystem.cs` | Switch `VfxPending` from `NativeStream.Writer` to the shared queue parallel writer; drop Begin/End + stream flush. |
| [005](005-aoe-spawn-expansion.md) | `AoeSpawnExpansionSystem.cs` | Emit trigger-0 spawn VFX inside `AoeExpansionJob` to the shared queue; drop main-thread buffer write. |
| [006](006-delete-dead-types.md) | `VfxFlushJob.cs`, `VfxEcsComponents.cs` | Delete `VfxFlushJob`/`VfxStreamFlushJob`, `VfxSingleton`, `VfxSpawnRequestElement`. |
| [007](007-tests.md) | 4 PlayMode test files | Remove `VfxSingleton`/buffer setup; rely on producer null-guard. |
| [008](008-docs.md) | 3 docs | Rewrite data flow; remove dead types; fix stale "trigger 0 not emitted" claim. |

Sequencing: 001 → 002 → (003, 004, 005) → 006 (only after no refs remain) →
007 → 008.

## Open questions / considerations

- **Single owner vs. split owner.** `CombatApplyFinalizeSystem` splits ownership
  (sim-group finalize + presentation-group bridge). For VFX there is no heavy
  sim-side finalize step — producers write, presentation drains — so we keep one
  owner (`CombatVfxDispatchSystem`, presentation group). Decided: single owner.
- **`VfxSingleton` deletion.** Grep confirms `VfxSingleton` /
  `VfxSpawnRequestElement` are used only by the VFX path and test setup; safe to
  delete. No other system depends on the entity.
- No unresolved load-bearing invariant requires a user decision: the shared-queue
  safety model is confirmed from existing `HitQueue` code, not assumed.

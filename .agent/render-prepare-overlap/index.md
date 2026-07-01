# Render Prepare Overlap (Presentation Group)

## Goal

Fill the worker-thread idle bubbles created by the two main-thread presentation
systems — `CombatApplyBridge` (~0.498 ms) and `CombatVfxDispatchSystem`
(~0.683 ms) — with the parallel `RenderPrepareJob` (~2 ms), **without leaving the
`PresentationSystemGroup`** and without introducing a manual sync barrier.

Today `RenderPrepareJob` is scheduled and immediately completed inside
`CombatBatchedRenderSystem.OnUpdate`
([CombatBatchedRenderSystem.cs:81-88](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs)),
so it runs alone at the very end of the frame while every worker had been idle
through the two managed systems just before it.

## High-level implementation

Split the existing single system into two, both in `PresentationSystemGroup`:

1. **`CombatRenderPrepareSystem`** (new, `OrderFirst = true`): owns the
   `renderPrepareQuery` and the kinematics/render/element type handles, schedules
   `RenderPrepareJob` with `ScheduleParallel`, assigns the handle to
   `Dependency`, and **does not** complete it. The job is now in flight for the
   remainder of the presentation phase.
2. **`CombatBatchedRenderSystem`** (existing, becomes consume-only): at the top of
   `OnUpdate` it `CompleteDependency()` (its incoming `Dependency` already
   includes the prepare job because it reads `CombatRenderElement`), then runs the
   unchanged registry/submit loop.

Between them, `CombatApplyBridge` and `CombatVfxDispatchSystem` run on the main
thread. Neither touches the render/kinematics component types, so neither pulls
the prepare job into its `Dependency`, and the job keeps running on the worker
threads across both — exactly the idle windows we want to fill. It is joined only
at the render system, right before GPU submission.

No new job-handle plumbing, no `[NativeDisableContainerSafetyRestriction]`, no
group boundary crossed. The only structural change is *where* the schedule call
lives.

## Constraints & invariants the change must respect

- **Kinematics are final before presentation.** `RenderPrepareJob` reads
  `CombatKinematicsComponent` (position/velocity). Existing entities' positions
  are last written by `ProjectileMovementSystem`
  ([ProjectileMovementSystem.cs:34](../../Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs));
  newly spawned entities by the spawn-apply systems, which force-complete their
  own jobs (`Dependency.Complete()` /
  `CombineDependencies(...).Complete()`,
  [ProjectileSpawnApplySystem.cs:87,155](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs)).
  By the time `PresentationSystemGroup` runs, all kinematics writers are complete.
  Source: system update-group attributes + spawn-apply completion calls.
- **Render component ownership is disjoint from the two overlapped systems.**
  `RenderPrepareJob` reads `CombatKinematicsComponent`/`CombatRenderComponent`
  (RO) and writes `CombatRenderElement` (RW) on `ProjectileTag`/`AoeTag` chunks.
  `CombatApplyBridge` touches only `TargetHealth`/`TargetCompanion` on target-proxy
  entities via `EntityManager` and calls `CompleteDependency()`
  ([CombatApplyFinalizeSingleSystem.cs:420-451](../../Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs)).
  `CombatVfxDispatchSystem` touches no ECS component at all — only its own
  `NativeQueue` + managed `CombatVfxRoot`
  ([CombatVfxDispatchSystem.cs:32-51](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs)).
  Source: code read of all three systems.
- **Render preparation must run after simulation/apply; submission in
  presentation.** Documented contract
  ([render-batch-data.md:45-48](../../Docs/contracts/render-batch-data.md)).
  Scheduling at `OrderFirst` of `PresentationSystemGroup` still satisfies
  "after apply" (presentation runs after the whole simulation group).
- **Sync points idle all workers.** Structural changes and any full sync force
  completion of every scheduled job
  ([ecs-notes.md:102-109](../../Docs/reference/simulation/ecs-notes.md)). The
  overlap only holds if nothing between prepare and submit triggers a global sync.
  The two overlapped systems perform no structural changes.
- **Presentation must not mutate finalized simulation state** beyond its own
  presentation buffers
  ([presentation-and-feedback.md:49-55](../../Docs/layers/presentation-and-feedback.md)).
  Writing `CombatRenderElement` (a render/presentation component) is permitted and
  is exactly what the current code already does.

## Mechanisms reused vs. introduced

- **Reused:** Unity ECS per-component-type `Dependency` chaining. The prepare job
  is left on `Dependency`; the consuming render system completes it via its own
  incoming `Dependency` / `CompleteDependency()` — the same call the code makes
  today, just moved to the consumer. No parallel handle-threading is introduced.
- **Reused:** the existing prepare/submit conceptual split already named in the
  render-batch-data contract.
- **Introduced:** one new `SystemBase` (`CombatRenderPrepareSystem`) holding the
  query + type handles that currently live on `CombatBatchedRenderSystem`. It owns
  a single responsibility (schedule the matrix job); the render system keeps
  submission. No new data type, no new component, no second data path.

## Design validation against invariants

- *Kinematics final before presentation* → prepare scheduled at `OrderFirst` of
  presentation reads already-complete positions. New-this-frame entities were
  spawned in simulation, so they are present in the query and get correct
  matrices this frame. **No new-entity frame delay** (unlike the
  simulation-side overlap alternative).
- *Disjoint ownership* → `CombatApplyBridge.CompleteDependency()` acts on the
  bridge's own type footprint (target health/companion), which does not include
  the render types, so it does not complete the prepare job. `CombatVfxDispatchSystem`
  registers no ECS access and no completion of render types. Overlap holds.
- *After-apply ordering* → satisfied; presentation is strictly after the
  simulation group.
- *No global sync between prepare and submit* → the two overlapped systems do no
  structural changes; `EntityManager.GetComponentObject<TargetCompanion>` completes
  only `TargetCompanion`'s dependency, not the render job. **This is the one point
  to confirm empirically** (see open questions / task 002).
- *No illegal simulation mutation* → only `CombatRenderElement` is written, same
  as today.

## Minimal/additive vs. refactor comparison

- **Minimal/additive approach** (keep scheduling in `CombatBatchedRenderSystem`,
  add a manual `JobHandle` field exposed to nobody, drop the immediate
  `CompleteDependency`):
  - resulting data flow: schedule + complete still colocated; to overlap you would
    hand a manual handle to an earlier point — but there is no earlier system, so
    this does not actually move the schedule earlier.
  - new concepts/types introduced: a manually-threaded handle duplicating what the
    `Dependency` chain already provides.
  - copies/translations added: none, but adds hand-managed sync bookkeeping.
  - long-term cost: a second, ad-hoc dependency path for one job that the built-in
    system already handles; easy to get wrong on later edits.
- **Refactor approach** (split schedule into an `OrderFirst` prepare system,
  render system consumes):
  - resulting data flow: one linear per-type dependency chain — prepare (writer)
    → submit (reader/completer); managed systems in between are transparent.
  - existing concepts/types changed: `CombatBatchedRenderSystem` loses the prepare
    query + handles (moved, not duplicated).
  - copies/translations removed/avoided: no manual handle plumbing; relies on the
    mechanism already governing every other job in the project.
  - long-term benefit: matches the documented prepare/submit contract; the overlap
    is a natural consequence of ordering, not of hand-tuned handles.
- **Decision: choose refactor.** It has fewer runtime data paths, introduces no
  parallel sync bookkeeping, and aligns with the existing contract. The single new
  system carries one responsibility and removes it from an overloaded system rather
  than hiding complexity.

## Default decision rule

There is one source of truth for the prepared matrices (`CombatRenderElement`),
written by one job, completed by one consumer through the built-in dependency
system. No second representation is introduced.

## Tasks

- [001-split-render-prepare-system.md](001-split-render-prepare-system.md) —
  Extract `CombatRenderPrepareSystem` (`OrderFirst`) that schedules
  `RenderPrepareJob` without completing it; make `CombatBatchedRenderSystem`
  consume-only.
- [002-verify-overlap.md](002-verify-overlap.md) — Profile to confirm the prepare
  job overlaps both managed systems; if a global sync collapses the overlap, apply
  the fallback ordering.

## Open questions / considerations

- **Does `CombatApplyBridge` force a global job sync?** Analysis says no
  (`EntityManager` managed-component access completes only that type; no structural
  change). If profiling shows the prepare job completing at the bridge instead of
  at the render system, fallback: order the prepare system
  `UpdateAfter(CombatApplyBridge)` + `UpdateBefore(CombatVfxDispatchSystem)`, which
  still guarantees the ~0.683 ms VFX-dispatch overlap. Captured in task 002.
- **Upside ceiling:** the two bubbles total ~1.18 ms; the job is ~2 ms, so this
  hides roughly the bubble width, not the whole job. The larger ~4 ms
  simulation-side bubble (finalize + spawn-apply) remains available as a separate,
  higher-effort follow-up (needs the spawn-apply inline-matrix change) and is
  explicitly out of scope here per the "stay in presentation group" constraint.
- **`CombatStatsGatherSystem`** runs after submission
  ([CombatStatsGatherSystem.cs:9-11](../../Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs));
  unaffected.

# Move Managed `OnUpdate` Work Into Burst Jobs

## Summary

Thirteen combat systems currently do real per-entity or per-event work as plain
managed C# inside `OnUpdate` itself — not inside a `[BurstCompile]` job struct.
This plan moves that work into Burst job structs.

Per the user's direction, **`.Run()` (Burst on the calling thread) is an
acceptable target shape**. A job that `.Run()`s is still Burst-compiled machine
code; it just executes on the main thread with ECS completing the component
dependencies it can see. Where the dependency story is already clear and the
codebase has an established handle-chaining pattern for that lane, tasks
upgrade to `.Schedule()`/`.ScheduleParallel()` instead, and that is called out
per task.

`.Run()` on a job is **not** an offender under this plan. `CombatBatchedRenderSystem`,
`StatusProcessSystem`, `CombatAoeVfxDispatchSystem`, and the five
`.Schedule(default).Complete()` spawn-apply lanes already have their work inside
Burst structs and are **out of scope**.

### Honest scoping note

Not every task here buys measurable frame time, and the plan does not pretend
otherwise. The work splits into two very different cost classes:

- **Event-count-bound** — scales with spawns per frame, which is the number the
  design pushes hardest (`Docs/performance.md`: ~50k projectiles at 120 fps).
  Tasks [002](./002-spawn-event-gather-job.md) and
  [003](./003-spawn-pool-topup-batch-enable.md) are here. **These are the real
  wins.**
- **Target-count-bound** — scales with player + mobs, which `Docs/performance.md`
  caps at *roughly 50*. Tasks [001](./001-resource-regen-job.md),
  [005](./005-target-proxy-apply-jobs.md), [006](./006-spatial-hash-gather-job.md),
  [007](./007-stats-counters.md), [008](./008-apply-bridge-redundant-pass.md) are
  here. At 50 entities the loops themselves are close to free.

For the target-count-bound group the payoff is **not** the loop — it is the
`.Complete()` that has to precede a main-thread native-container read, and the
consistency of having one uniform "work lives in a Burst struct" shape. Where a
task removes a sync point, it says so. Where it does not, it says that too:
Burst-ifying a 50-iteration loop that sits behind a `Dependency.Complete()`
moves the instructions and leaves the stall. Task
[006](./006-spatial-hash-gather-job.md) is explicitly in that category and is
ranked last for that reason.

## Rationale

### Why one uniform pattern (extract to a job struct), not `[BurstCompile]` on `OnUpdate`

Two of the offenders (`ResourceRegenSystem`, `TargetSpatialHashSystem`) are
`ISystem` structs, so the cheapest conceivable fix is `[BurstCompile]` on the
struct and on `OnUpdate`. This plan does **not** do that, for three reasons:

1. It only works for `ISystem`. Nine of the thirteen offenders are `SystemBase`
   managed classes, which can never be Burst-compiled. Taking the `ISystem`
   shortcut would leave the codebase with two different answers to "where does
   work live," which is exactly the second-data-path problem
   `Docs/coding-standards.md` warns about under *System Encapsulation*.
2. Every system in `Assets/Scripts/System/` already puts its work in a
   `[BurstCompile]` job struct. Extracting to a job struct **conforms to the
   existing mechanism**; Burst-compiling `OnUpdate` introduces a parallel one.
3. `TargetSpatialHashSystem` reads `static readonly ProfilerMarker<int>` fields
   and `TargetProxyCreateApplySystem`/`ExternalSpawnGateSystem` sit next to
   managed-object code. Job extraction draws the managed/unmanaged line
   explicitly at the struct boundary instead of relying on what Burst happens
   to accept inside a whole `OnUpdate`.

### Why the dual event path (scope `DynamicBuffer` + lane `NativeQueue`) stays

The four expansion systems each merge two sources: a
`DynamicBuffer<*SpawnEvent>` on the `CombatScope` entity and the lane's
`NativeQueue<*SpawnEvent>`. This looks like a duplicate data path and was
evaluated as one. It is not, and collapsing it was rejected:

- The `NativeQueue` is written by parallel Burst jobs
  (`TimedSpawnSystem`, the collision systems, `StatusProcessSystem`) through
  `.AsParallelWriter()`, chained via the lane's `ProducerHandle`.
- The `DynamicBuffer` is written by **managed gameplay code** —
  `CombatRoot.Spawn` / `SpawnLingeringAoe` / `SpawnImpactAoe`
  (`CombatRoot.cs:190`, `:235`, `:1117`, `:1134`) — from arbitrary points in the
  managed Update phase. `CombatRoot` has no `Update()`; these are public API
  methods called by `SkillDriver` and friends whenever a cast happens.

`EntityManager.GetBuffer<T>` completes the relevant job dependencies
automatically through the ECS type system. A raw `NativeQueue.Enqueue` from
managed gameplay code has no such protection — every caller would have to
complete the lane's `ProducerHandle` by hand at an unpredictable point in the
frame. The two paths encode two genuinely different safety contracts. Keeping
both is correct; what changes is only that the **merge** stops being a
main-thread element-by-element copy.

## Constraints & Invariants

- **Structural changes are main-thread only and cause a sync point.**
  `CreateEntity`, `DestroyEntity`, `AddComponent`, `RemoveComponent` cannot run
  inside a job. Source: `Docs/reference/simulation/ecs-notes.md` §*What Is a
  Structural Change*. → Bounds tasks [003](./003-spawn-pool-topup-batch-enable.md)
  and [005](./005-target-proxy-apply-jobs.md): the structural call stays on the
  main thread, only the surrounding per-element work moves.
- **Enableable-component toggling is not structural** and is ~100x cheaper at
  query granularity than per entity (0.03 ms vs 3.5 ms per 1M, same doc,
  §*Optimization Strategies*). → Directly motivates
  [003](./003-spawn-pool-topup-batch-enable.md).
- **Simulation must never read managed `TargetCompanion` or live Unity
  objects.** Source: `Docs/architecture/phase-order.md` §*Phase Ownership*;
  `Docs/contracts/target-proxy.md` §*Restrictions*. → `CombatApplyBridge` and
  `SpawnRejectionBridge` stay managed. Task
  [008](./008-apply-bridge-redundant-pass.md) removes only a redundant native
  pre-pass, never the managed replay.
- **Target proxy create/update apply before `TargetSpatialHashSystem`; delete
  applies in `PresentationSystemGroup` after `CombatApplyBridge`.** Source:
  `Docs/contracts/target-proxy.md` §*Ordering*. → Task
  [005](./005-target-proxy-apply-jobs.md) must not change system ordering
  attributes, only internals.
- **Lane singletons own their native containers and expose explicit `JobHandle`
  fields; producers combine into `ProducerHandle` on the main thread, sinks
  complete before draining.** Source: `Docs/coding-standards.md` §*System
  Encapsulation*. → Every task that schedules rather than runs must publish its
  handle into the same lane field the existing producers use. No new handle
  fields are introduced by this plan.
- **`NativeQueue` contents are an unordered set under parallel writes.**
  Source: `Docs/coding-standards.md` §*Why NativeQueue for These Sinks (Not
  Ordering)*. → The gather job in [002](./002-spawn-event-gather-job.md) is free
  to drain with `ToArray` instead of `TryDequeue` and free to append queue and
  buffer contents in either order.
- **`CombatScope` is a single ref-counted entity.** Source:
  `Docs/reference/simulation/project-ecs-implementation.md` §*AOE Pool Pattern*;
  `CombatTargetProxy.TryGetScopeEntity` asserts `CalculateEntityCount() == 1`.
  → The `for (scopeIndex...)` outer loops in the expansion and proxy systems are
  one-iteration loops. They are not the cost and are not what these tasks
  optimize; the inner per-event loops are.
- **Combat paths are allocation-light; no per-entity allocations in spawn
  expansion/apply.** Source: `Docs/coding-standards.md` §*Allocation Rule*. →
  The gather job must not allocate per event. Using `NativeList` +
  `AsDeferredJobArray` (task 002) removes an allocation and a sizing pass rather
  than adding one.
- **Any code creating a native handle owns its disposal on every teardown
  path.** Source: `Docs/coding-standards.md` §*Native And ECS Handle Ownership*.
  → Task [002](./002-spawn-event-gather-job.md) changes the lane's event
  container from `NativeArray` to `NativeList`; its `Dispose` must be threaded
  through both the normal path and each system's `OnDestroy`.
- **Do not use `unsafe` in gameplay or shared runtime systems.** Source:
  `Docs/coding-standards.md` §*Unity Object Access*. → Task
  [007](./007-stats-counters.md) must not reach for `Interlocked` over a raw
  pointer to accumulate a parallel counter; it reuses the existing
  `NativeQueue<int>.ParallelWriter` mechanism instead.
- **Generic Burst jobs require explicit registration.** `[RegisterGenericJobType]`
  is needed per concrete instantiation or the job silently falls back to managed
  execution. → Load-bearing for the shared generic gather job in
  [002](./002-spawn-event-gather-job.md); called out in that task's acceptance
  criteria because a missed registration produces *correct but un-Bursted* code,
  i.e. a silent no-op of the whole task.

## Mechanisms Reused vs. Introduced

**Reused:**

- The `[BurstCompile] private struct XJob : IJob{,Entity,Chunk}` nested-in-system
  shape every system in `Assets/Scripts/System/` already uses.
- Lane singleton `ProducerHandle`/`PendingHandle` chaining — tasks 002, 004, 005
  publish into the existing fields, adding none.
- `TargetSpatialHashSingleton.ConsumerHandle`, already used by
  `ImpactAoeCollisionSystem.cs:103` to register a read against the hash without
  completing it — task [004](./004-external-spawn-gate-job.md) adopts this
  instead of the `BuildHandle.Complete()` it does today.
- `TargetedAcquisition` is already `[BurstCompile]` and already operates on a
  `Snapshot` of native arrays (`TargetedAcquisition.cs:13-37`) — task 004 calls
  it from a job with no changes to it.
- `NativeQueue<int>.ParallelWriter` for parallel counter accumulation, exactly
  as `CombatStatsSingleton.TargetedLinkCounts` already does — task 007.
- `SpawnPoolTopUp` stays the single owner of pool top-up; task 003 changes its
  body, not its callers or its role.

**Introduced:**

- One generic `[BurstCompile] struct GatherSpawnEventsJob<T> : IJob` shared by
  the four expansion systems (task 002). This is new surface, justified because
  it *replaces* four near-identical hand-written managed gathers with one
  implementation — net mechanism count goes down. It is the only new type in
  this plan.

## Design Validation

| Invariant | Held? |
|---|---|
| Structural changes stay main-thread | Yes — 003 keeps `CreateEntity`; 005 keeps `DestroyEntity`/`AddComponentObject` on the main thread, batched. |
| Enableable toggling at query granularity | 003 is precisely this change. |
| Simulation never reads managed companions | Yes — 008 touches only a `NativeList` pre-pass; the managed replay is untouched. Bridges stay `SystemBase`. |
| Proxy apply ordering | Yes — 005 changes internals only; no `[UpdateBefore]`/`[UpdateAfter]` attribute is edited by any task. |
| Lane handle chaining | Yes — 002/004/005 publish into existing `ProducerHandle`/`PendingHandle`/`ConsumerHandle`. No new handle fields. |
| `NativeQueue` order is not meaningful | Relied on by 002's `ToArray` drain; explicitly licensed by coding standards. |
| One `CombatScope` entity | Acknowledged — tasks target inner loops, and none of them claim a win from the scope loop. |
| Allocation-light hot path | 002 removes a sizing pass and an allocation; no task adds a per-event allocation. |
| Native handle ownership | 002 explicitly re-threads disposal for the changed container type through `OnDestroy`. |
| No `unsafe` | 007 uses `NativeQueue<int>.ParallelWriter`, not raw atomics. |
| Generic Burst registration | Called out as an acceptance criterion in 002, since omission fails silently. |

## Minimal/Additive vs. Refactor Comparison

**Minimal/additive approach** (rejected): wrap each offending `OnUpdate` body in
a per-system `[BurstCompile] IJob` and `.Run()` it, changing nothing else.

- resulting data flow: identical to today. Every `.Complete()` stays. The four
  expansion systems keep four separate hand-written gathers, now duplicated
  inside four separate job structs. `SpawnPoolTopUp` keeps its per-entity
  enable loop, just Bursted.
- new concepts/types introduced: none, but four near-identical gather
  implementations get *copied into job structs*, entrenching the duplication in
  a place that is harder to unify later.
- copies/translations added: none removed either — the main-thread pre-count
  pass, the exact-size `NativeArray` allocation, and the element-by-element copy
  all survive, merely Bursted.
- long-term cost: the plan's headline win (task 003, chunk-granularity enable)
  never happens, because it is not a "move code into a job" change at all. The
  four gathers stay four. Every future lane adds a fifth hand-written gather.

**Refactor approach** (chosen): extract to Burst job structs *and*, where the
managed shape was itself the problem, fix the shape.

- resulting data flow: `CombatRoot`/`ExternalSpawnGateSystem` → scope
  `DynamicBuffer` **and** parallel producers → lane `NativeQueue`, both merged by
  one generic Burst gather job into a `NativeList`, handed to the existing
  expansion job as `AsDeferredJobArray()` with no main-thread sizing pass at
  all. Pool top-up creates entities, then disables `Active` at query
  granularity in one call. Proxy update applies through a Burst job chained on
  `Dependency` instead of behind `Dependency.Complete()`.
- existing concepts/types changed or removed: four hand-written managed gathers
  removed; `SpawnPoolTopUp`'s per-entity enable loop removed; three
  `Dependency.Complete()` calls removed (tasks 004, 005); one
  `BuildHandle.Complete()` mid-loop removed (task 004); the redundant
  `HasAnyStatusRange` pass removed (task 008); four
  `CalculateEntityCount()` calls removed (task 007).
- copies/translations removed or avoided: the main-thread pre-count pass and the
  per-element `TryDequeue`/`buf[i]` copies in all four expansion systems; the
  exact-size `NativeArray` allocation they existed to serve.
- long-term benefit: one gather implementation for every current and future
  spawn lane; work lives in Burst structs uniformly, so "is this system doing
  managed work?" has one answer everywhere; four sync points fewer per
  simulation frame.

**Decision: refactor.** Reason: the additive version cannot deliver the two
tasks that actually scale with the design target (002's single gather, 003's
chunk-granularity enable) because in both cases the *shape* of the managed code,
not its compilation, is the cost. The refactor's extra scope is confined to code
that is already being rewritten.

## Default Decision Rule Applied

One representation pair was examined and **deliberately not collapsed**: the
scope `DynamicBuffer<*SpawnEvent>` and the lane `NativeQueue<*SpawnEvent>` both
describe "a pending spawn event." They are kept separate because they carry
different *safety* contracts (ECS-tracked managed write vs. parallel-job write),
not merely different callers — a concrete reason of exactly the kind the rule
admits. This is recorded so a later reader does not re-open it as an oversight.

## Task List

Ordered by payoff, not by dependency. Tasks are independent unless noted.

| # | Task | Cost class | Buys |
|---|---|---|---|
| [002](./002-spawn-event-gather-job.md) | One generic Burst gather job for all four expansion systems | event-bound | Removes 4 managed gathers + 4 sizing passes |
| [003](./003-spawn-pool-topup-batch-enable.md) | `SpawnPoolTopUp` per-entity enable → query granularity | event-bound | ~100x on cold-create enable, per ecs-notes table |
| [004](./004-external-spawn-gate-job.md) | `ExternalSpawnGateSystem` → Burst job | event-bound | Removes an in-loop `BuildHandle.Complete()` |
| [001](./001-resource-regen-job.md) | `ResourceRegenSystem` → single `IJobEntity` | target-bound | Establishes the pattern; smallest change in the plan |
| [005](./005-target-proxy-apply-jobs.md) | Proxy update → job; proxy delete → batch destroy | target-bound | Removes 2 `Dependency.Complete()` |
| [007](./007-stats-counters.md) | Stats drain → job; cleanup delete-count → `NativeQueue` | target-bound | Removes 4 `CalculateEntityCount()` |
| [008](./008-apply-bridge-redundant-pass.md) | Delete `CombatApplyBridge.HasAnyStatusRange` | target-bound | Removes one redundant full pass |
| [006](./006-spatial-hash-gather-job.md) | `TargetSpatialHashSystem.GatherTargets` → job | target-bound | **Consistency only** — see task file |

**Dependency notes.** 002 and 003 both touch the spawn path but different files
and can land in either order. 001 is the recommended first landing regardless of
priority: it is the smallest possible instance of the pattern every other task
follows, so it settles the shape cheaply. Everything else is independent.

## Open Questions

None blocking. Two judgment calls were resolved from code rather than escalated:

- *Collapse the dual spawn-event path?* — No. Resolved by reading
  `CombatRoot.cs:190/235/1117/1134`: the buffer path is the managed-safe write
  path and has no `NativeQueue` equivalent that is safe to call from arbitrary
  managed gameplay code. Recorded under *Default Decision Rule Applied*.
- *`[BurstCompile]` on `ISystem.OnUpdate` instead of job extraction?* — No.
  Resolved on uniformity grounds: it is inapplicable to the nine `SystemBase`
  offenders. Recorded under *Rationale*.

One item is deliberately left to the user rather than assumed, and is called out
in its task file rather than here: `TargetProxyCreateApplySystem` is **excluded**
from task 005 on cost/benefit grounds (creates are per-actor-registration, not
per-frame). See [005](./005-target-proxy-apply-jobs.md) §*Deliberately Excluded*
for the reasoning and what it would take if the user wants it included anyway.

## Testing

Per `Docs/project-overview.md`, agents do not run tests. Each task file lists
the existing test assemblies that cover the system it touches. After all tasks
land, the command to run is:

```
Unity.exe -runTests -batchmode -projectPath "e:/UnityHub/projects/play-ground" -testPlatform PlayMode -testResults "e:/UnityHub/projects/play-ground/TestResults/burst-onupdate-work-results.xml"
```

and the EditMode equivalent with `-testPlatform EditMode` and
`burst-onupdate-work-editmode-results.xml`. Report only against the exported XML.

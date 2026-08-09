# 006 — `TargetSpatialHashSystem.GatherTargets` → Burst job

## Why — and an honest warning about what this does not buy

`TargetSpatialHashSystem.GatherTargets` (`:182-202`) is managed:

```csharp
state.EntityManager.CompleteDependencyBeforeRO<TargetPosition>();
state.EntityManager.CompleteDependencyBeforeRO<TargetCollisionShape>();
state.EntityManager.CompleteDependencyBeforeRO<TargetFaction>();

using NativeArray<Entity> entities = targetQuery.ToEntityArray(Allocator.Temp);
using NativeArray<TargetPosition> positions = targetQuery.ToComponentDataArray<TargetPosition>(Allocator.Temp);
// ...two more...

for (int i = 0; i < targetCount; i++)
{
    singleton.TargetEntities[i] = entities[i];
    // ...three more...
}
```

plus `CalculateAoeCapacity` (`:216-228`), a second managed O(targets) pass doing
grid math.

**This task is ranked last and is consistency work, not a performance fix.**
Two reasons, both worth stating before anyone starts it:

1. **Volume is ~50.** `Docs/performance.md` puts player + mobs "below roughly
   50" as the early target. A 50-iteration copy loop is not measurable.
2. **The cost is the three `CompleteDependencyBeforeRO` calls, and moving the
   loop into a job does not remove them.** Those exist because
   `ToComponentDataArray` reads component data on the main thread. A job would
   still need the same data, so the dependency still has to be satisfied — it
   would just be expressed as `state.Dependency` chaining instead of an explicit
   complete. That *is* strictly better (it lets the scheduler overlap rather
   than blocking), but it is a scheduling improvement, not the removal of work.

Do not expect a profiler delta from this task at current target counts. Its
value is that after 001–005 land, this is the last place in
`Assets/Scripts/System/` where per-element work happens outside a Burst struct,
and leaving one exception undermines the "one uniform shape" rationale the whole
plan rests on.

**If frame time is the only goal, skip this task.** It is included because the
plan claims uniformity, and uniformity with one exception is not uniformity.

## Scope

- `Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs`

## Change

### Fold the gather into the existing build jobs' dependency chain

The three `CompleteDependencyBeforeRO` calls and the four `To*Array` extractions
exist only to hand data to the three build jobs that follow
(`BuildProjectileCollisionHashJob`, `BuildTrackingHashJob`,
`BuildAoeOccupiedHashJob`, scheduled at `:137`, `:145`, `:151`). Those jobs
consume `singleton.TargetPositions.AsArray()` etc.

Replace the extract-then-copy with a `[BurstCompile] IJobChunk` that writes the
four persistent `NativeList`s directly from chunk arrays:

```csharp
[BurstCompile]
private struct GatherTargetsJob : IJobChunk
{
    [ReadOnly] public EntityTypeHandle EntityHandle;
    [ReadOnly] public ComponentTypeHandle<TargetPosition> PositionHandle;
    [ReadOnly] public ComponentTypeHandle<TargetCollisionShape> ShapeHandle;
    [ReadOnly] public ComponentTypeHandle<TargetFaction> FactionHandle;
    [NativeDisableParallelForRestriction] public NativeList<Entity> TargetEntities;
    // ...three more, pre-resized by the main thread...
    [ReadOnly] public NativeArray<int> ChunkBaseIndex;   // prefix sum, see below

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                        bool useEnabledMask, in v128 chunkEnabledMask) { ... }
}
```

Scheduled with `.Schedule()` (single-threaded) this needs no prefix sum — a
running cursor works, and at 50 targets there is nothing to parallelise.
**Specify `.Schedule()`, not `.ScheduleParallel()`**; the prefix-sum apparatus
above is what parallel would require and is not worth it here.

`ResizeSnapshotLists` (`:204-214`) stays on the main thread — `NativeList`
resizing is not job-safe and the count comes from
`targetQuery.CalculateEntityCount()` which the system already has (`:96`).

### `CalculateAoeCapacity`

This runs *between* the gather and `ClearContainers`/`EnsureCapacity` (`:117-122`)
and its result sizes a hashmap, so the main thread needs the number before it can
schedule anything. Options:

- **Fold it into `GatherTargetsJob`**, writing to a `NativeReference<int>`, then
  complete just that job before `EnsureCapacity`. Trades the managed loop for a
  targeted `.Complete()` — no better than today.
- **Leave it managed.** At 50 targets it is 50 iterations of integer grid math.

**Leave it managed** and note it in the code with a comment explaining why, so a
future reader does not "fix" it into a job and add a sync point. This is a case
where the managed loop is the right answer.

### Do not Burst-compile `OnUpdate` itself

The struct holds `static readonly ProfilerMarker<int>` fields (`:50-53`) used via
`GatherMarker.Auto(targetCount)` (`:112`, `:129`). Rather than establishing
whether that specific generic-payload marker survives Burst compilation, keep
`OnUpdate` managed and put the work in the job struct — consistent with every
other task in this plan and with the rationale in
[index.md](./index.md) §*Why one uniform pattern*.

## Acceptance Criteria

- No `for` loop copying element-by-element into `singleton.Target*` lists.
- The three `CompleteDependencyBeforeRO<T>()` calls are gone, replaced by
  `state.Dependency` chaining into the gather job.
- The four `To*Array(Allocator.Temp)` extractions are gone — the job reads chunk
  arrays directly.
- The three build jobs still depend on the gather completing, and
  `singleton.BuildHandle` is still the combined handle of all three
  (`:154-155`), so downstream consumers' `BuildHandle.Complete()` /
  `ConsumerHandle` contracts are unchanged.
- The `targetCount == 0` early-out (`:99-110`) still clears all containers,
  zeroes `MaxTargetRadius`, and publishes `BuildHandle` before returning.
- `CalculateAoeCapacity` remains main-thread, with a comment saying why.
- `MaxTargetRadius` is still written by `BuildProjectileCollisionHashJob`, not
  by the new gather job — it is that job's output
  (`TargetSpatialHashSystem.cs:136`) and moving it would change which handle
  guards it.

## Dependencies

None functionally. **Sequence it after 004**, which adds a new
`ConsumerHandle` registration against this singleton — landing 004 first means
this task's handle-contract verification covers the new consumer too.

## Scope/Complexity

Medium. One file, one new job struct, careful handle rewiring. Low risk, low
reward — see the warning at the top.

## Test Coverage

The spatial hash underpins all collision and tracking, so
`Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`,
`ProjectileContinuousSimulationTests.cs`, and `AoeSimulationTests.cs` all cover
it indirectly and would catch a gather that drops or reorders targets. A
regression here is loud, not subtle — projectiles stop hitting things.

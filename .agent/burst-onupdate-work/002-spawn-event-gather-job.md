# 002 — One generic Burst gather job for all four expansion systems

## Why

Four expansion systems each merge the lane's `NativeQueue<T>` with the
`CombatScope` entity's `DynamicBuffer<T>` into one `NativeArray<T>`, and all four
do it as managed main-thread code. Three of them do it element-by-element:

| System | Lines | Shape |
|---|---|---|
| `ImpactAoeSpawnExpansionSystem` | `AoeSpawnExpansionSystem.cs:259-291` | `while TryDequeue` + per-index `buf[i]` |
| `LingeringAoeSpawnExpansionSystem` | `AoeSpawnExpansionSystem.cs:430-463` | same |
| `TargetedSpawnExpansionSystem` | `TargetedSpawnExpansionSystem.cs:183-215` | same |
| `ProjectileSpawnExpansionSystem` | `ProjectileSpawnExpansionSystem.cs:113-149` | bulk `NativeArray.Copy`, still a managed pre-count pass |

This is the **event-count-bound** work in the plan — it scales with spawns per
frame, the number `Docs/performance.md` pushes to ~50k projectiles at 120 fps,
not with the ~50 target cap.

All four also run a **main-thread pre-count pass** purely to size an exact
`NativeArray`:

```csharp
int bufferCount = 0;
for (int s = 0; s < scopes.Length; s++)
{
    bufferCount += EntityManager.GetBuffer<T>(scopes[s]).Length;
}
int totalEvents = queueCount + bufferCount;
var events = new NativeArray<T>(totalEvents, Allocator.TempJob);
```

Moving the gather into a job lets the job size the output itself, which deletes
the pre-count pass rather than Bursting it.

## Scope

- New file: `Assets/Scripts/System/Spawning/GatherSpawnEventsJob.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs` (both systems in it)
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`

## Change

### New shared job

```csharp
[BurstCompile]
internal struct GatherSpawnEventsJob<T> : IJob
    where T : unmanaged, IBufferElementData
{
    public NativeQueue<T> Queue;
    public BufferTypeHandle<T> BufferHandle;
    [ReadOnly] public NativeArray<ArchetypeChunk> ScopeChunks;
    public NativeList<T> Events;

    public void Execute()
    {
        Events.Clear();

        NativeArray<T> queued = Queue.ToArray(Allocator.Temp);
        Events.AddRange(queued);
        queued.Dispose();
        Queue.Clear();

        for (int c = 0; c < ScopeChunks.Length; c++)
        {
            BufferAccessor<T> accessor = ScopeChunks[c].GetBufferAccessor(ref BufferHandle);
            for (int i = 0; i < accessor.Length; i++)
            {
                DynamicBuffer<T> buffer = accessor[i];
                Events.AddRange(buffer.AsNativeArray());
                buffer.Clear();
            }
        }
    }
}
```

`Queue.ToArray` + `AddRange` replaces the per-element `TryDequeue` loop, and
`buffer.AsNativeArray()` + `AddRange` replaces the per-index copy. Draining with
`ToArray` instead of `TryDequeue` is explicitly licensed: `Docs/coding-standards.md`
§*Why NativeQueue for These Sinks (Not Ordering)* states these queues carry no
meaningful order and sinks must treat contents as an unordered set.

### Generic Burst registration — load bearing

A generic job that is never registered compiles and runs **correctly but
unmanaged-free**: Burst silently skips it and it executes as managed IL. That
would make this entire task a no-op with no error to notice. Add, in the same
file:

```csharp
[assembly: RegisterGenericJobType(typeof(GatherSpawnEventsJob<ProjectileSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(GatherSpawnEventsJob<ImpactAoeSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(GatherSpawnEventsJob<LingeringAoeSpawnEvent>))]
[assembly: RegisterGenericJobType(typeof(GatherSpawnEventsJob<TargetedSpawnEvent>))]
```

### Container type change: `NativeArray<T>` → `NativeList<T>`

Each expansion system currently allocates `events` as an exact-size
`NativeArray<T>` because the main thread already knows the count. Once the job
does the counting, the output must be a `NativeList<T>` and the downstream
expansion job takes it as a deferred array.

Per system, `OnUpdate` becomes roughly:

```csharp
lane.PendingHandle.Complete();
// ... existing Commands dispose/reset, ProducerHandle complete ...

var events = new NativeList<T>(Allocator.TempJob);
using NativeArray<ArchetypeChunk> scopeChunks =
    _scopeQuery.ToArchetypeChunkArray(Allocator.TempJob);

JobHandle gatherHandle = new GatherSpawnEventsJob<T>
{
    Queue = lane.EventQueue,
    BufferHandle = GetBufferTypeHandle<T>(false),
    ScopeChunks = scopeChunks,
    Events = events
}.Schedule(Dependency);

// existing expansion job now consumes the deferred array
Dependency = new XExpansionJob
{
    Events = events.AsDeferredJobArray(),
    ...
}.Schedule(JobHandle.CombineDependencies(gatherHandle, vfxProducerHandle));

Dependency = events.Dispose(Dependency);
Dependency = scopeChunks.Dispose(Dependency);
```

`AsDeferredJobArray()` is the reason the pre-count pass can go: the expansion
job reads a length the gather job decided, with no main-thread sync between
them.

### The early-out changes meaning

Today each system checks `totalEvents == 0` on the main thread and returns
early, skipping the expansion job entirely. After this change the count is not
known on the main thread. Two options, and this task chooses the second:

1. Keep a cheap main-thread `EventQueue.Count == 0 && scopeQuery.IsEmpty` guard.
   Still correct, but `scopeQuery.IsEmpty` does not tell you the *buffer* is
   empty — the scope entity always exists — so this guard is weaker than the one
   it replaces and would still schedule on frames with zero events.
2. **Drop the early-out and always schedule.** The expansion job's own
   `for (int ci = 0; ci < Events.Length; ci++)` already no-ops on an empty array.
   An empty Burst `IJob` is a few microseconds of scheduling overhead.

Option 2 is chosen: it is simpler, removes a branch whose guard condition can no
longer be evaluated accurately, and the cost of an empty job is far below the
per-element managed copy this task removes. Note this trades a *zero-cost* idle
frame for a *near-zero-cost* one — call it out in review rather than treating it
as free.

### `ProjectileSpawnExpansionSystem` splits two output lists

That system's expansion job writes into `DiscreteCommands` **and**
`ContinuousCommands` (`ProjectileSpawnExpansionSystem.cs:294-301`). That is
downstream of the gather and unaffected — only the `Events` input shape changes.

## Acceptance Criteria

- One `GatherSpawnEventsJob<T>` exists, `[BurstCompile]`, used by all four
  expansion systems.
- All four `RegisterGenericJobType` assembly attributes are present. **Verify by
  Burst Inspector** that each instantiation appears — a missing entry is silent.
- No expansion system's `OnUpdate` contains a `while (…TryDequeue(…))` loop, a
  `for` loop over `buf.Length`, or a `bufferCount` pre-count pass.
- Each lane's `Commands`/`PendingHandle`/`ProducerHandle` handling is otherwise
  unchanged, and each still publishes `PendingHandle` for its apply system.
- The new `NativeList<T> events` and the `NativeArray<ArchetypeChunk>` are both
  disposed on the normal path (via `Dispose(JobHandle)`) **and** on the
  early-return template-missing path (`if (!SystemAPI.TryGetSingleton(out …
  templates))`), which currently disposes `events` and returns.
- `OnDestroy` of each system still completes `PendingHandle` and `ProducerHandle`
  before disposing lane containers — unchanged, but re-verify, since the
  container the handles guard changed type.
- Scope `DynamicBuffer`s are cleared exactly once per frame, as today.
  The buffer is cleared *inside* the job now; confirm no system also clears it
  on the main thread afterwards.

## Dependencies

None. Independent of every other task. Largest single win in the plan.

## Scope/Complexity

Medium-large. One new file plus four `OnUpdate` rewrites that follow the same
template. The four rewrites are near-identical, which is the point — but they
are in three files and `AoeSpawnExpansionSystem.cs` holds two of them.

## Test Coverage

- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` — seeds
  `GetBuffer<ProjectileSpawnEvent>(scopeEntity)` directly (`:475`), so it covers
  the buffer half of the merge.
- `Assets/Tests/PlayMode/AoeSimulationTests.cs:1488`, `:1501` — same for both
  AOE lanes.
- `Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs:198` — impact AOE.
- `Assets/Tests/EditMode/TargetedSpawnPipelineEditModeTests.cs:288` — targeted.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:188`, `:205`
  assert on `GetBuffer<ProjectileSpawnEvent>(scopeEntity).Length` **after** the
  gate runs but before expansion. Check these still hold — they read the buffer
  the gather job now clears, so their position relative to system updates
  matters.

The queue half of the merge is covered indirectly by any test that spawns
through collision or timed spawn.

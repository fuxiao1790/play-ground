# 007 — Stats drain → job; cleanup delete-count → `NativeQueue`

## Why

Two small managed passes, both in the stats path.

**`CombatStatsGatherSystem`** (`Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs:77-88`)
drains a `NativeQueue<int>` element-by-element on the main thread, behind a
`ProducerHandle.Complete()`:

```csharp
snapshot.TargetedLinkProducerHandle.Complete();
while (snapshot.TargetedLinkCounts.TryDequeue(out int links))
{
    snapshot.TargetedLinksResolved += links;
}
```

plus three `CalculateEntityCount()` calls (`:78-80`).

**`CombatPoolCleanupSystem`** (`Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs:143-161`)
computes `LastDeletedCount` by calling `CalculateEntityCount()` four times —
twice before the trim and twice after — purely to diff the pool size:

```csharp
int before = _poolQuery.CalculateEntityCount()
    + _targetedPoolQuery.CalculateEntityCount();
// ...trim...
LastDeletedCount = before
    - _poolQuery.CalculateEntityCount()
    - _targetedPoolQuery.CalculateEntityCount();
```

The trim job already knows exactly how many entities it destroyed. Asking the
query layer to re-count the entire pool twice to rediscover that number is the
kind of thing `Docs/coding-standards.md` §*Performance Budget Rule* means by
"debug counters should exist before content stress tests become hard to
explain" — the counter exists, it is just derived the expensive way. Unlike most
of this plan this one *does* scale with pool size, which tracks the 50k
projectile target.

## Scope

- `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`

## Change

### `CombatPoolCleanupSystem` — count in the job

`PoolTrimJob` is `ScheduleParallel` (`:181`), so the counter must be
parallel-safe. `Docs/coding-standards.md` §*Unity Object Access* forbids
`unsafe` in shared runtime systems, ruling out raw `Interlocked` over a pointer.

**Reuse the mechanism already in the codebase for exactly this:**
`CombatStatsSingleton.TargetedLinkCounts` is a `NativeQueue<int>` written by a
parallel job through `.ParallelWriter` and drained by the gather system. Apply
the same shape:

```csharp
[BurstCompile]
private struct PoolTrimJob : IJobChunk
{
    // ...existing fields...
    public NativeQueue<int>.ParallelWriter DeletedCounts;

    public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, ...)
    {
        // ...existing threshold logic...
        int destroyed = 0;
        for (int i = 0; i < count; i++)
        {
            if (!activeMask[i])
            {
                Ecb.DestroyEntity(unfilteredChunkIndex, entities[i]);
                destroyed++;
            }
        }

        if (destroyed > 0)
        {
            DeletedCounts.Enqueue(destroyed);
        }
    }
}
```

The system already calls `Dependency.Complete()` before `ecb.Playback` (`:155-157`),
so draining the queue right after costs no additional sync point. Allocate the
queue `Allocator.TempJob` per update and dispose it after the drain — it is only
live within one `OnUpdate`, so it does not need to join
`CombatStatsSingleton`'s persistent containers or its disposal path.

This removes all four `CalculateEntityCount()` calls. `before` disappears
entirely.

### `CombatStatsGatherSystem` — drain in a job

Smaller and lower value. The drain is bounded by the number of targeted chain
links resolved this frame. Convert to:

```csharp
[BurstCompile]
private struct DrainLinkCountsJob : IJob
{
    public NativeQueue<int> Counts;
    public NativeReference<int> Total;

    public void Execute()
    {
        int total = 0;
        while (Counts.TryDequeue(out int links))
        {
            total += links;
        }
        Total.Value = total;
    }
}
```

`.Run()` it — this system is in `PresentationSystemGroup` and already completes
`TargetedLinkProducerHandle` immediately before, so there is no dependency to
manage and nothing to overlap with. This is exactly the case the plan's premise
covers: Burst-compiled, main thread, zero scheduling complexity.

The three `CalculateEntityCount()` calls at `:78-80` **stay**. They are cheap
chunk-header sums, not per-entity walks, and they feed
`ActiveProjectiles`/`ActiveAoes`/`ActiveTargeted` which `CombatPoolCleanupSystem`
reads as its calm-down gate input (`:113-118`). Replacing them with job-side
counting would add a handle to complete for no gain.

## Acceptance Criteria

- `CombatPoolCleanupSystem` contains no `CalculateEntityCount()` call.
- `LastDeletedCount` equals the exact number of entities the trim job destroyed.
  This is now *more* accurate than today: the current diff assumes "nothing else
  mutates the pool mid-update" (stated in the comment at `:141-142`), which the
  job-side count does not have to assume.
- `CombatStatsSingleton.EntitiesDeleted` accumulates the same value it does now.
- The `NativeQueue<int>` used for delete counts is `Allocator.TempJob`, disposed
  in the same `OnUpdate`, and is **not** added to `CombatStatsSingleton` — that
  struct's containers are `Allocator.Persistent` with disposal in
  `CombatStatsGatherSystem.OnDestroy` (`:55-68`), and mixing lifetimes there is
  how leaks start.
- The calm-down gate's early `return` path (`:130-133`) still runs before any
  allocation, so a gated frame allocates nothing.
- `CombatStatsGatherSystem` still publishes the full
  `CombatStatsDisplaySingleton` mirror in one write (`:92-105`) — the comment at
  `:90-91` records that this is the only write it ever receives, and splitting it
  would let the debug overlay observe a partial snapshot.

## Dependencies

None. Independent. The two halves are separable — the cleanup half carries most
of the value and can land alone.

## Scope/Complexity

Small. Two files, two small jobs, no cross-system contract changes.

## Test Coverage

- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs` and
  `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs` — these assert on
  cleanup behavior and likely on deleted counts. Check whether any test asserts
  `LastDeletedCount` directly; if so, the job-side count may produce a
  *different and more correct* number in edge cases, which would be a legitimate
  test update rather than a regression.

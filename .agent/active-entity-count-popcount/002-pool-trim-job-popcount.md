# 002 — `PoolTrimJob`: popcount the chunk mask instead of testing every bit

Depends on: nothing.
Scope: small/medium. One file: `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`.

## Problem

[CombatPoolCleanupSystem.cs:192-225](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L192-L225):

```csharp
EnabledMask activeMask = chunk.GetEnabledMask(ref ActiveHandle);

int count = chunk.Count;
int activeCount = 0;
for (int i = 0; i < count; i++)
{
    if (activeMask[i])
    {
        activeCount++;
    }
}

int threshold = (int)(count * ActiveThresholdPercent / 100f);
if (activeCount >= threshold)
{
    return;
}

NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
for (int i = 0; i < count; i++)
{
    if (!activeMask[i])
    {
        Ecb.DestroyEntity(unfilteredChunkIndex, entities[i]);
    }
}
```

Two per-entity bit-test passes over the whole chunk. The count pass is the todo
item; the destroy pass tests every entity to find the disabled subset.

The mask cannot be popcounted where it is: `EnabledMask` exposes only `this[int]`
and `GetBit(int)` (`EnabledMask.cs:111-169`); its backing `v128` is
package-internal, and reaching it needs `unsafe`, forbidden by
[coding-standards.md:137](../../Docs/coding-standards.md).

Root cause of the awkwardness: the queries declare
`WithAll<Active>() + EntityQueryOptions.IgnoreComponentEnabledState`
([CombatPoolCleanupSystem.cs:82-91](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L82-L91)),
which deliberately throws away the package's per-entity filtering so the job can
redo it by hand.

## Change

Let the query express "disabled `Active`", so the mask the job is *handed* is the
one it wants, and popcount that.

**1. Query shape** — `OnCreate`, [CombatPoolCleanupSystem.cs:82-91](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L82-L91):

```csharp
// WithDisabled<Active>() makes IJobChunk's chunkEnabledMask the disabled-Active
// mask for the chunk, so the job popcounts it instead of testing every bit.
// Active is the only enableable component in these queries (ProjectileTag, AoeTag,
// TargetedTag are plain IComponentData), so this replaces IgnoreComponentEnabledState
// without changing which chunks match on any other type.
_poolQuery = new EntityQueryBuilder(Allocator.Temp)
    .WithDisabled<Active>()
    .WithAny<ProjectileTag, AoeTag>()
    .Build(this);
_targetedPoolQuery = new EntityQueryBuilder(Allocator.Temp)
    .WithDisabled<Active>()
    .WithAll<TargetedTag>()
    .Build(this);
```

`WithDisabled<T>()` requires `T` present but matches only entities where it is
disabled (`EntityQueryBuilder.cs:1069`), and requests ReadOnly access.

**2. Drop the now-unused handle.** `_activeHandle`
([:56](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L56)), its
`GetComponentTypeHandle<Active>(true)` in `OnCreate`
([:94](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L94)), its
`.Update(this)` ([:147](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L147)),
and the job's `ActiveHandle` field all go. The job no longer reads `Active` data —
the query declares the access and the mask arrives as a job parameter.

**3. Job body:**

```csharp
public void Execute(
    in ArchetypeChunk chunk,
    int unfilteredChunkIndex,
    bool useEnabledMask,
    in v128 chunkEnabledMask)
{
    // The query matches disabled-Active entities, so chunkEnabledMask marks the
    // chunk's disabled slots. useEnabledMask is false when every entity matches
    // (a fully drained chunk) and the mask contents are undefined there, so that
    // case reads the count straight off the chunk.
    int count = chunk.Count;
    int disabledCount = useEnabledMask
        ? math.countbits(chunkEnabledMask.ULong0) + math.countbits(chunkEnabledMask.ULong1)
        : count;
    int activeCount = count - disabledCount;

    // Busy chunk: leave its disabled entities as a warm reuse buffer.
    int threshold = (int)(count * ActiveThresholdPercent / 100f);
    if (activeCount >= threshold)
    {
        return;
    }

    NativeArray<Entity> entities = chunk.GetNativeArray(EntityHandle);
    ChunkEntityEnumerator enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, count);
    while (enumerator.NextEntityIndex(out int i))
    {
        Ecb.DestroyEntity(unfilteredChunkIndex, entities[i]);
    }
}
```

`ChunkEntityEnumerator` walks set bits with `tzcnt`
(`ChunkEntityEnumerator.cs:58-76`) and fills both masks with ones when
`useEnabledMask` is false (`:33-34`), so the drained-chunk case iterates all
entities, which is correct — they are all disabled. No `!activeMask[i]` test
remains, and no entity outside the destroy set is visited.

**4. Reword the stale comment** at
[CombatPoolCleanupSystem.cs:141-142](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L141-L142).
The queries now count disabled entities, not the whole pool:

> Only disabled entities are destroyed and nothing else mutates the pool
> mid-update, so the drop in the disabled-slot count equals the number deleted.

`before - after` around
[:143-144](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L143-L144)
and [:159-161](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L159-L161)
stays exact and needs no other edit.

**5. `using` directives.** `Unity.Mathematics` is already imported
([:20](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L20)) for
`math.max`/`math.lerp`, and `Unity.Burst.Intrinsics`
([:16](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L16)) for
`v128`. Nothing new required.

## Behaviour deltas — intended, verify each

1. **Chunks with zero disabled entities stop reaching the job.** The chunk-cache
   iterator skips all-zero masks (`UnsafeChunkCacheIterator.cs:116-120`). Those
   chunks previously ran the count loop and early-returned, so the outcome is
   identical and the work disappears.
2. **`IsEmpty` becomes a tighter gate.**
   [:136](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L136)
   now means "no disabled pool entities" rather than "no pool entities at all", so
   a fully-active scene early-outs instead of scheduling two no-op jobs. Strictly
   better; keep the check.
3. **`CalculateEntityCount()` gets slower per call, deliberately.** It leaves the
   no-filter fast path (`ChunkIterationUtility.cs:953-959`) for the chunk-walk +
   popcount branch. Four calls per trim pass, against a pass that already schedules
   two parallel jobs across every pool chunk, and only on calm-down frames.
   Accepted.
4. **Calm-down gate untouched.** It returns before any query work
   ([:113-134](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L113-L134))
   and reads only `CombatStatsSingleton`. Do not modify it in this task.

## Acceptance criteria

- No loop over `chunk.Count` and no `EnabledMask` indexing anywhere in the job.
- `activeCount` is derived from `math.countbits`, with the `useEnabledMask == false`
  branch present. **This branch is required, not defensive** — a fully drained pool
  chunk hits it, which is exactly what `Cleanup_TrimsTheTargetedPool` constructs.
- Destroy pass visits only disabled entities, via `ChunkEntityEnumerator`.
- `_activeHandle` and `PoolTrimJob.ActiveHandle` are gone; the job still carries
  `[BurstCompile]` and compiles under Burst.
- Trim decisions are bit-identical to before for the same pool state.
- `LastDeletedCount` and `CombatStatsSingleton.EntitiesDeleted` unchanged for the
  same scenario.
- Comment at :141-142 updated; no `ECS Lifecycle:` comment needs editing (no
  component declaration changes).
- No new `unsafe`.

## Tests that cover this

Existing, must stay green:
- `Cleanup_TrimsTheTargetedPool` — [TargetedLifetimeAndCleanupEditModeTests.cs:79-100](../../Assets/Tests/EditMode/TargetedLifetimeAndCleanupEditModeTests.cs#L79-L100).
  2 entities, both disabled, threshold 100 → both destroyed. Exercises
  `useEnabledMask == false`.
- `ActiveProjectileContinuesToSimulateAfterDisabledPoolTrim` and the rest of
  [Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs).
  1 active + 10 disabled, threshold 40 → exercises `useEnabledMask == true`.
- Targeted pool trim path in
  [Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs:452-456](../../Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs#L452-L456).

Gap worth filling (optional, same task): an EditMode case with a **mixed** chunk
that must *not* be trimmed — active count at or just above the threshold — to pin
the `>=` boundary. The two existing tests both expect a trim, so no test currently
fails if the threshold comparison inverts.

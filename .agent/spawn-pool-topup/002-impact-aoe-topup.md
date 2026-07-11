# 002 — Impact AOE: top-up then fill

**File:** [Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)
(`ImpactAoeSpawnApplySystem` + `ImpactAoeSpawnJob`)

**Depends on:** [001](001-spawn-pool-topup-helper.md).

## Change

Replace the ECB cold path with a top-up before the reuse fill. Target `OnUpdate`
region (current lines ~88–144):

```csharp
using (SpawnMarker.Auto())
{
    // 1) Grow pool to demand BEFORE fetching chunks/handles (structural change).
    int created = SpawnPoolTopUp.EnsureDisabledSlots(
        EntityManager, _impactArchetype, _deadSlotQuery, totalRequests);

    // 2) Fill — existing reuse job, now guaranteed enough slots.
    using (ReuseJobMarker.Auto())
    {
        using NativeArray<ArchetypeChunk> chunks =
            _deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob);
        using var reused = new NativeReference<int>(Allocator.TempJob);

        new ImpactAoeSpawnJob
        {
            Configs = commands,
            Chunks = chunks,
            ReuseCount = reused,
            // ... existing handles unchanged ...
        }.Schedule(default).Complete();

        LastReuseCount = reused.Value;      // == totalRequests
    }

    // 3) Counters.
    SpawnReuseCounter.Value = LastReuseCount;
    SpawnTopUpCounter.Value = created;      // renamed from SpawnColdCreateCounter
    LastColdCreateCount = created;

    if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
    {
        stats.ValueRW.EntitiesSpawnedViaReuse += LastReuseCount;
        stats.ValueRW.EntitiesSpawnedViaEcb += created; // now "via top-up create"
    }
}
```

Deletions:
- `using var createEcb = new EntityCommandBuffer(...)` and its `Playback`.
- The cold-create loop (current lines ~120–126).
- `AoeSpawnApplyUtility.RecordImpactReset` (no longer called; confirm no other
  caller before removing).

Unchanged:
- `ImpactAoeSpawnJob` and its chunk-fill logic — byte-for-byte.
- `_deadSlotQuery` (`WithAll<AoeTag>().WithDisabled<Active>().WithNone<LingeringAoeTag>()`)
  and `_impactArchetype`.

Counter rename: `SpawnColdCreateCounter` → `SpawnTopUpCounter`
(`"ImpactAoeSpawnApplySystem.TopUp"`).

## Acceptance criteria

- Compiles; `RecordImpactReset` removed with no dangling references.
- **Ordering:** `EnsureDisabledSlots` runs before `ToArchetypeChunkArray` /
  `GetComponentTypeHandle` (else stale handles → corrupt/missing spawns).
- On a cold start frame: `Reuse == totalRequests`, `TopUp == totalRequests`,
  no ECB in the profiler.
- On a warm frame (enough disabled slots): `TopUp == 0`, no `CreateEntity`
  structural change that tick.
- `AoeSimulationTests` PlayMode suite passes (impact spawn + collision + gate).

## Scope

Small — one system; job untouched.

# 003 — Lingering AOE: top-up then fill

**File:** [Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)
(`LingeringAoeSpawnApplySystem` + `LingeringAoeSpawnJob`)

**Depends on:** [001](001-spawn-pool-topup-helper.md). Same shape as
[002](002-impact-aoe-topup.md), lingering archetype/query.

## Change

In `LingeringAoeSpawnApplySystem.OnUpdate` (current lines ~309–369):

```csharp
using (SpawnMarker.Auto())
{
    int created = SpawnPoolTopUp.EnsureDisabledSlots(
        EntityManager, _lingeringArchetype, _deadSlotQuery, totalRequests);

    using (ReuseJobMarker.Auto())
    {
        using NativeArray<ArchetypeChunk> chunks =
            _deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob);
        using var reused = new NativeReference<int>(Allocator.TempJob);

        new LingeringAoeSpawnJob
        {
            Configs = commands,
            Chunks = chunks,
            ReuseCount = reused,
            // ... existing handles unchanged ...
        }.Schedule(default).Complete();

        LastReuseCount = reused.Value;
    }

    SpawnReuseCounter.Value = LastReuseCount;
    SpawnTopUpCounter.Value = created;   // renamed from SpawnColdCreateCounter
    LastColdCreateCount = created;

    if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
    {
        stats.ValueRW.EntitiesSpawnedViaReuse += LastReuseCount;
        stats.ValueRW.EntitiesSpawnedViaEcb += created;
    }
}
```

Deletions:
- `createEcb` + `Playback`.
- The cold-create loop (current lines ~346–351).
- `AoeSpawnApplyUtility.RecordLingeringReset` (confirm no other caller).

Unchanged:
- `LingeringAoeSpawnJob` — including its lifetime, pulse-VFX, timed-spawn seed,
  and arming writes. These already run on every filled slot, so cold-created
  slots now get them through the same path (this is the correctness win over the
  ECB, which duplicated that logic in `RecordLingeringReset`).
- `_deadSlotQuery` (`WithAll<AoeTag,LingeringAoeTag>().WithDisabled<Active>()`)
  and `_lingeringArchetype`.

Counter rename: `SpawnColdCreateCounter` → `SpawnTopUpCounter`
(`"LingeringAoeSpawnApplySystem.TopUp"`).

## Acceptance criteria

- Compiles; `RecordLingeringReset` removed cleanly.
- Ordering: top-up before chunk/handle fetch.
- Cold start: `Reuse == totalRequests`, `TopUp == totalRequests`.
- Warm: `TopUp == 0`.
- `AoeSimulationTests` passes — specifically lingering lifetime countdown and
  **timed-spawn children still fire** (confirms `TimedSpawnComponent` enable +
  `TimedSpawnStateComponent` seed applied on freshly created slots via the job).

## Scope

Small — mirrors 002.

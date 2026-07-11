# 004 — Projectile: top-up then fill

**File:** [Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs)
(`ProjectileSpawnApplySystem` + `ProjectileSpawnJob`)

**Depends on:** [001](001-spawn-pool-topup-helper.md). Same shape as
[002](002-impact-aoe-topup.md), with extra deletions (this lane used an instance
helper + a dedicated sub-marker).

## Change

In `ProjectileSpawnApplySystem.OnUpdate` (current lines ~109–171):

```csharp
using (SpawnMarker.Auto())
{
    int created = SpawnPoolTopUp.EnsureDisabledSlots(
        EntityManager, _archetype, _deadSlotQuery, totalRequests);

    using (ReuseJobMarker.Auto())
    {
        using NativeArray<ArchetypeChunk> chunks =
            _deadSlotQuery.ToArchetypeChunkArray(Allocator.TempJob);
        using var reused = new NativeReference<int>(Allocator.TempJob);

        new ProjectileSpawnJob
        {
            Commands = commands,
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
- The `using (ColdCreateMarker.Auto())` block and its loop (lines ~146–153).
- The `ColdCreateMarker` field (lines ~34–35).
- Instance method `CreateProjectileEntity` (lines ~175–180).
- Static `RecordCommonProjectileReset` (lines ~182–235) and
  `RecordTimedSpawnReset` (lines ~237–247) — confirm no other caller first.

Unchanged:
- `ProjectileSpawnJob` — including the `SeedContactGateTargetId`
  `gate.Add(...)` path, tracking-enable bit, timed-spawn seed, and arming. All
  already applied on every filled slot, so cold-created slots get them through
  the same path.
- `_deadSlotQuery` (`WithAll<ProjectileTag>().WithDisabled<Active>()`) and
  `_archetype`.
- `HitPayloadFor` / `InitialTimedSpawnStateFor` / `DeterministicJitter` (still
  used by `ProjectileSpawnJob`).

Counter rename: `SpawnColdCreateCounter` → `SpawnTopUpCounter`
(`"ProjectileSpawnApplySystem.TopUp"`).

## Acceptance criteria

- Compiles; `CreateProjectileEntity`, `RecordCommonProjectileReset`,
  `RecordTimedSpawnReset`, and `ColdCreateMarker` all removed with no dangling
  references.
- Ordering: top-up before chunk/handle fetch.
- Cold start: `Reuse == totalRequests`, `TopUp == totalRequests`; no ECB /
  no `ColdCreate` marker in the profiler.
- Warm: `TopUp == 0`.
- `ProjectileCollisionSimulationTests` passes — pierce, contact-gate seed,
  tracking-enable all correct on freshly created projectiles.

## Scope

Small-plus — one system plus deleting one instance helper, two static record
helpers, and one marker.

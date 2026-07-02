# 004 — Rewrite render submit to plain-int scatter

Rewrite `CombatBatchedRenderSystem` so it no longer filters by shared component
or calls `ToComponentDataArray`. `CombatRenderPrepareSystem` is UNCHANGED — it
still computes per-entity `CombatRenderElement` matrices in parallel.

## Approach (correctness-first, main-thread scatter)

Per user: rendering perf may regress here; keep it obviously correct. The
parallel compaction is deferred (Follow-up 1).

Replace `SubmitBatchId` / the shared-filter loop
([CombatBatchedRenderSystem.cs:57-74](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L57-L74)) with a single scatter:

```
OnUpdate:
  CompleteDependency();
  reset LastActiveProjectileCount / LastActiveAoeCount;
  clear per-batch scratch buffers (reused; keyed by batch id / registry entry);

  scatter projectileRenderQuery -> buffers, accumulating LastActiveProjectileCount;
  scatter aoeRenderQuery        -> buffers, accumulating LastActiveAoeCount;

  foreach (batchId, entry) in registry.Entries:
     if buffer[batchId].Length > 0: SubmitAll(buffer[batchId], entry);
```

Scatter over a query (main thread):
```csharp
var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
foreach (var chunk in chunks)
{
    var elems    = chunk.GetNativeArray(ref elementHandle);
    var batchIds = chunk.GetNativeArray(ref batchIdHandle);
    var e = new ChunkEntityEnumerator(useEnabledMask, mask_for(CombatRenderActiveTag), chunk.Count);
    while (e.NextEntityIndex(out int i))
        buffer[batchIds[i].Value].Add(elems[i].objectToWorld);
}
```

- Add a `ComponentTypeHandle<CombatRenderBatchId>` and
  `ComponentTypeHandle<CombatRenderElement>` (read-only), updated each frame.
- Queries keep `WithAll<ProjectileTag>` / `WithAll<AoeTag>` so the per-domain
  active counts feeding `CombatStatsGatherSystem` are preserved; they now filter
  by tag only (no `CombatRenderBatchId` shared filter).
- `SubmitAll` ([:76](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L76)) is reused as-is (takes a `NativeArray<Matrix4x4>` slice).

## Buffer storage (no per-frame GC)
- Per-batch reusable buffers, e.g. a `Dictionary<int, NativeList<Matrix4x4>>`
  (batch id → list), created lazily per registry id and `Clear()`ed each frame
  (capacity retained). Dispose all in `OnDestroy`.
- Alternatively a `NativeList<Matrix4x4>` per `CombatRenderResourceEntry`.

## What is removed
- `SetSharedComponentFilter` / `ResetFilter` usage.
- `ToComponentDataArray<CombatRenderElement>` (the copy the user flagged).
- The two `using NativeArray` temporaries per batch.

## Acceptance criteria
- Projectiles and AOEs render with correct sprites, layers, Z ordering.
- Disabled (`CombatRenderActiveTag` off) entities are not drawn.
- `LastActiveProjectileCount` / `LastActiveAoeCount` still reflect active counts.
- No shared-component API remains in the render system.
- No per-frame managed allocation in `OnUpdate` (buffers reused).

## Depends on
001. Compiles together with 002, 003.

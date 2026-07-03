# 003 — Render System Rewrite

## Change

Rewrite `CombatBatchedRenderSystem` so `OnUpdate` scatters both queries into one
`NativeList<CombatInstanceData>`, uploads it once, and issues a **single**
`Graphics.RenderMeshIndirect`. Delete the `MaterialPropertyBlock` per-instance path, the
`_uvRectScratch` scratch, `MaxInstancesPerDraw`, and the 1023-chunked submit loop.

## OnUpdate

```csharp
protected override void OnUpdate()
{
    CompleteDependency();

    LastActiveProjectileCount = 0;
    LastActiveAoeCount = 0;

    var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
    if (registry.SharedMesh == null) return;

    _instances.Clear();

    renderElementHandle.Update(this);
    renderComponentHandle.Update(this);
    renderActiveHandle.Update(this);

    LastActiveProjectileCount = Scatter(projectileRenderQuery);
    LastActiveAoeCount = Scatter(aoeRenderQuery);

    int activeCount = _instances.Length;
    if (activeCount == 0) return;               // nothing active → no draw

    EnsureInstanceCapacity(activeCount);        // task 002
    _instanceBuffer.SetData(_instances.AsArray(), 0, 0, activeCount);
    PopulateArgs(registry, activeCount);        // task 002

    Submit(registry);                            // one RenderMeshIndirect
}
```

`CompleteDependency()` stays (still the correct sync before reading render data on the main
thread). Note this is also where the idle-pool sync cost lands — out of scope here (see index).

## Scatter (combined matrix + UV into one struct)

Same active-only scan as today, but emit `CombatInstanceData` instead of parallel arrays:

```csharp
private int Scatter(EntityQuery query)
{
    int activeCount = 0;
    using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
    foreach (ArchetypeChunk chunk in chunks)
    {
        NativeArray<CombatRenderElement> elements = chunk.GetNativeArray(ref renderElementHandle);
        NativeArray<CombatRenderComponent> renders = chunk.GetNativeArray(ref renderComponentHandle);
        EnabledMask activeMask = chunk.GetEnabledMask(ref renderActiveHandle);
        for (int i = 0; i < chunk.Count; i++)
        {
            if (!activeMask[i] || renders[i].IsRenderable == 0)
                continue;

            float4 uv = renders[i].UvRect;
            _instances.Add(new CombatInstanceData
            {
                objectToWorld = elements[i].objectToWorld,
                uvRect = new Vector4(uv.x, uv.y, uv.z, uv.w)
            });
            activeCount++;
        }
    }
    return activeCount;
}
```

Both queries append into the **same** `_instances` list, so projectiles and AOEs share one
buffer and one draw. `LastActiveProjectileCount`/`LastActiveAoeCount` remain accurate for the
HUD (`CombatStatsGatherSystem`).

## Submit (one indirect draw)

```csharp
private void Submit(CombatRenderResourceRegistry registry)
{
    if (_rebindBuffer)                              // buffer (re)created this frame (task 002)
    {
        _matProps.SetBuffer("_InstanceData", _instanceBuffer);
        _rebindBuffer = false;
    }

    RenderParams rp = new RenderParams(registry.SharedMaterial)
    {
        matProps = _matProps,
        shadowCastingMode = ShadowCastingMode.Off,
        receiveShadows = false,
        layer = registry.Layer,
        worldBounds = new Bounds(
            Vector3.zero,
            new Vector3(CombatRenderResourceRegistry.BoundsHalfExtent,
                        CombatRenderResourceRegistry.BoundsHalfExtent,
                        CombatRenderResourceRegistry.BoundsHalfExtent) * 2f)
    };

    Graphics.RenderMeshIndirect(rp, registry.SharedMesh, _argsBuffer, 1);
}
```

`_matProps` is kept only as the buffer-bind carrier (`SetBuffer`), not for per-instance arrays.
Binding via `matProps` leaves the shared material untouched; rebinding only on buffer recreate
avoids per-frame churn (open question 1 in index — `matProps` chosen).

## Platform guard

`RenderMeshIndirect` requires `SystemInfo.supportsIndirectArgumentsBuffer`. Add a one-time check
in `OnCreate` (log + disable, or fall back) so an unsupported platform fails loudly rather than
silently drawing nothing. Assumed true on the desktop/URP target.

## Acceptance Criteria

- Exactly **one** `Graphics.RenderMeshIndirect` call per update regardless of active instance
  count (verify in Frame Debugger: one combat draw, not one-per-1023 and not one-per-kind).
- All active projectiles and AOEs render at correct position/rotation/scale/Z and correct atlas
  sub-rect; two different kinds on screen show two different sprites.
- No per-frame managed allocation in the hot path beyond the `Allocator.Temp` chunk arrays that
  already existed; `_instances` and the GPU buffers are reused across frames.
- `activeCount == 0` frames issue no draw and no `SetData`.
- No reference to `MaxInstancesPerDraw`, `_uvRectScratch`, or `SetVectorArray` remains.

## Dependencies

001 (shader/material to draw with) + 002 (struct + buffers). 004 may adjust the bind site.

## Scope

Medium (the core of the rework).

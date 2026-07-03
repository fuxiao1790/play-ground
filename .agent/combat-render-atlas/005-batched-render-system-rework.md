# Batched Render System Rework

*(Updated 2026-07-03: the scatter pass reads `UvRect` directly from each entity's
`CombatRenderComponent` — no registry dictionary lookup per entity, per frame. `CombatRenderBatchId`
is no longer read by this system at all.)*

## Change

Rework `CombatBatchedRenderSystem.cs` to collapse the per-kind
`Dictionary<int, NativeList<Matrix4x4>>` into one transform buffer + one
parallel UV-rect buffer, filled by reading each active entity's own
`CombatRenderComponent.UvRect` directly (computed once, at spawn time, by the
task-003 registry — not looked up here).

### New buffer shape

```csharp
private NativeList<Matrix4x4> _transforms;   // was: Dictionary<int, NativeList<Matrix4x4>>
private NativeList<Vector4> _uvRects;        // new, parallel to _transforms
private Vector4[] _uvRectScratch;            // reusable managed scratch, length == MaxInstancesPerDraw, allocated once in OnCreate (avoids per-frame GC — MaterialPropertyBlock.SetVectorArray requires a managed Vector4[])
private MaterialPropertyBlock _matProps;     // reused across chunks/frames
```

`OnCreate`: allocate `_transforms`/`_uvRects` (`Allocator.Persistent`),
`_uvRectScratch = new Vector4[MaxInstancesPerDraw]`, `_matProps = new MaterialPropertyBlock()`.
Registry singleton creation and the two `EntityQueryBuilder` queries are
unchanged from the pre-atlas baseline, **except** they now require
`CombatRenderComponent` (to read `UvRect`) instead of `CombatRenderBatchId`
(which this system no longer touches at all).

`OnDestroy`: dispose `_transforms`/`_uvRects`.

### `OnUpdate`

```csharp
protected override void OnUpdate()
{
    CompleteDependency();
    LastActiveProjectileCount = 0;
    LastActiveAoeCount = 0;

    var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
    if (registry.SharedMesh == null) return; // nothing registered yet

    _transforms.Clear();
    _uvRects.Clear();

    renderElementHandle = GetComponentTypeHandle<CombatRenderElement>(true);
    renderComponentHandle = GetComponentTypeHandle<CombatRenderComponent>(true);
    renderActiveHandle = GetComponentTypeHandle<CombatRenderActiveTag>(true);

    LastActiveProjectileCount = Scatter(projectileRenderQuery);
    LastActiveAoeCount = Scatter(aoeRenderQuery);

    if (_transforms.Length > 0)
        SubmitAll(registry);
}
```

No `EnsureAtlasCurrent()`/repack call — that concept no longer exists (task
002/003 revision: the atlas is a static, manually-assigned texture, never
rebuilt at runtime).

### `Scatter` (reads `CombatRenderComponent` instead of doing a registry lookup)

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

            float4 uvRect = renders[i].UvRect;
            _transforms.Add(elements[i].objectToWorld);
            _uvRects.Add(new Vector4(uvRect.x, uvRect.y, uvRect.z, uvRect.w));
            activeCount++;
        }
    }
    return activeCount;
}
```

This is the same active-only scan as the pre-atlas baseline (`Scatter`),
extended to emit the UV rect alongside the matrix in the same loop iteration
— not a second pass, and now genuinely just a flat per-entity field read
(no dictionary, no registry parameter needed by this method at all).

**`IsRenderable == 0` replaces the old "dictionary-miss skip."** In the
prior (registry-table-lookup) revision of this task, an entity whose
`CombatRenderBatchId` didn't resolve in the registry's UV-rect table was
silently skipped via a failed `TryGetValue`. With the UV rect stored
directly on the entity, there's no lookup to fail — the equivalent "this
entity has no visual" signal is `CombatRenderComponent.IsRenderable == 0`
(already set by `Get*RenderComponent` for the `renderId == 0` case), so the
scatter loop checks that explicitly instead.

### `SubmitAll` (unchanged from the prior revision of this task)

```csharp
private void SubmitAll(CombatRenderResourceRegistry registry)
{
    RenderParams rp = new RenderParams(registry.SharedMaterial)
    {
        matProps = _matProps,
        shadowCastingMode = ShadowCastingMode.Off,
        receiveShadows = false,
        layer = registry.Layer,
        worldBounds = new Bounds(
            Vector3.zero,
            new Vector3(CombatRenderResourceRegistry.BoundsHalfExtent, CombatRenderResourceRegistry.BoundsHalfExtent, CombatRenderResourceRegistry.BoundsHalfExtent) * 2f)
    };

    NativeArray<Matrix4x4> transforms = _transforms.AsArray();
    NativeArray<Vector4> uvRects = _uvRects.AsArray();

    for (int start = 0; start < transforms.Length; start += MaxInstancesPerDraw)
    {
        int count = math.min(MaxInstancesPerDraw, transforms.Length - start);
        for (int i = 0; i < count; i++)
            _uvRectScratch[i] = uvRects[start + i];

        _matProps.SetVectorArray("_UvRect", _uvRectScratch);
        Graphics.RenderMeshInstanced(rp, registry.SharedMesh, 0, transforms, count, start);
    }
}
```

## Acceptance Criteria

- All active projectile/AOE render entities across every registered kind are
  drawn in as few `Graphics.RenderMeshInstanced` calls as the 1023-instance
  cap requires.
- No visual regression versus the pre-rework per-kind system: position,
  rotation, scale, Z-order, transparency all match.
- Entities with `IsRenderable == 0` are silently skipped (matches the
  intent of the old renderId-0/dictionary-miss skip — never throws, never
  draws garbage).
- No per-frame managed allocation in the hot path beyond what already
  existed (the `_uvRectScratch` array and `NativeList`s are allocated once
  in `OnCreate`).
- No per-frame registry access beyond `SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>()`
  itself (no dictionary/table read inside the scatter loop).

## Dependencies

003 (registry rework — needs `SharedMesh`/`SharedMaterial`/`Layer`;
`CombatRenderComponent.UvRect` itself comes from task 003's `Register()`/
`Get*RenderComponent` changes, already flowing onto entities via existing
apply-system plumbing with no apply-system changes needed).

## Scope

Medium.

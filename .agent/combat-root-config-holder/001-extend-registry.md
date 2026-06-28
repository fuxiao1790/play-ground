# 001 — Extend CombatRenderResourceRegistry

## File
`Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Dependency
None — first step.

## Changes

Add to `CombatRenderResourceRegistry`:

```csharp
private readonly Dictionary<int, CombatSpriteRenderResources> _resourcesById = new();
private int _nextRenderId = 1;

public int Register(
    Sprite sprite,
    Vector2 visualScale,
    float visualRotationDegrees,
    Material sourceMaterial,
    string meshName,
    int layer)
{
    if (sprite == null) return 0;
    CombatSpriteRenderResources resources = BatchedSpriteRenderer.BuildResources(
        sprite, visualScale, visualRotationDegrees, sourceMaterial, meshName);
    int renderId = _nextRenderId++;
    _resourcesById[renderId] = resources;
    Entries[renderId] = new CombatRenderResourceEntry
    {
        Resources = resources,
        Layer = layer,
        BoundsHalfExtent = 100000f
    };
    return renderId;
}
```

No `BatchIdFor` helper — the faction overhaul already made `CombatRenderBatchId.Value = cmd.RenderTypeId` (plain renderId). The registry key is just `renderId`. No faction parameter on `Register`.

Add render-component builders:

```csharp
public CombatRenderComponent GetProjectileRenderComponent(int renderId, int projectileId)
{
    if (!_resourcesById.TryGetValue(renderId, out var res)) return default;
    return new CombatRenderComponent
    {
        IsRenderable = 1,
        AlignToVelocity = 1,
        VisualScale = new float2(res.VisualScale.x, res.VisualScale.y),
        VisualRotationSin = res.VisualRotationSin,
        VisualRotationCos = res.VisualRotationCos,
        RenderZ = CombatRoot.ProjectileRenderZ
            - (projectileId % CombatRoot.ProjectileRenderZSlots) * CombatRoot.ProjectileRenderZStep
    };
}

public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
{
    if (!_resourcesById.ContainsKey(renderId)) return default;
    return new CombatRenderComponent
    {
        IsRenderable = 1,
        AlignToVelocity = 0,
        VisualScale = new float2(geometry.VisualScale.x, geometry.VisualScale.y),
        VisualRotationSin = geometry.VisualRotationSin,
        VisualRotationCos = geometry.VisualRotationCos,
        RenderZ = CombatRoot.AoeRenderZ
    };
}
```

Add teardown:

```csharp
// Removes and destroys all render resources. Called from CombatRoot.OnDestroy.
public void Unregister()
{
    foreach (var res in _resourcesById.Values)
        res.Destroy();
    _resourcesById.Clear();
    Entries.Clear();
}
```

No faction parameter — one root owns all entries; clear everything on teardown.

## Acceptance Criteria
- `CombatRenderResourceRegistry` compiles standalone with the new methods.
- `Register`, `GetProjectileRenderComponent`, `GetAoeRenderComponent`, `Unregister` are all accessible as `public`.
- No callers yet wired — this step only adds the API.

## Scope
Small. ~60 lines added to one file.

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

public static int BatchIdFor(CombatFaction faction, int renderId) =>
    ((int)faction << 16) | renderId;

public int Register(Sprite sprite, CombatFaction faction, int layer, string meshName)
{
    // sprite == null → renderId 0 (no visual)
    CombatSpriteRenderResources resources = BatchedSpriteRenderer.BuildResources(
        sprite, visualScale, rotationDeg, material, meshName);
    int renderId = _nextRenderId++;
    _resourcesById[renderId] = resources;
    Entries[BatchIdFor(faction, renderId)] = new CombatRenderResourceEntry
    {
        Resources = resources,
        Layer = layer,
        BoundsHalfExtent = 100000f   // effectively infinite; field kept for future use
    };
    return renderId;
}
```

Actual signature (matching what callers need):

```csharp
public int Register(
    Sprite sprite,
    Vector2 visualScale,
    float visualRotationDegrees,
    Material sourceMaterial,
    string meshName,
    CombatFaction faction,
    int layer)
```

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
// Removes and destroys all render resources registered for the given faction.
public void Unregister(CombatFaction faction)
{
    var toRemove = new List<int>(); // reuse scratch or iterate+remove in two passes
    foreach (var (batchId, _) in Entries)
    {
        if ((CombatFaction)(batchId >> 16) == faction)
            toRemove.Add(batchId);
    }
    foreach (int batchId in toRemove)
    {
        int renderId = batchId & 0xFFFF;
        if (_resourcesById.TryGetValue(renderId, out var res))
        {
            res.Destroy();
            _resourcesById.Remove(renderId);
        }
        Entries.Remove(batchId);
    }
}
```

Note: `BatchIdFor` was previously private static on `CombatRoot`. Moving it here as public static is the right home — it encodes the registry key format.

## Acceptance Criteria
- `CombatRenderResourceRegistry` compiles standalone with the new methods.
- `BatchIdFor`, `Register`, `GetProjectileRenderComponent`, `GetAoeRenderComponent`, `Unregister` are all accessible as `public`.
- No callers yet wired — this step only adds the API.

## Scope
Small. ~60 lines added to one file.

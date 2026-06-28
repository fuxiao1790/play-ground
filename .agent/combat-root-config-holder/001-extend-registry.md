# 001 — Extend CombatRenderResourceRegistry

## File
`Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Dependency
None — first step. Adds API only; no callers wired yet.

## Single store: use `Entries`

The registry already holds the resources in
`Entries[renderId].Resources` (`CombatRenderResourceEntry.Resources`). Do **not**
add a second `Dictionary<int, CombatSpriteRenderResources>` — that would recreate
the duplicate store this refactor removes. Everything below reads/writes `Entries`.

## Changes to `CombatRenderResourceRegistry`

### Counter + constants

```csharp
private int _nextRenderId = 1;

public const string ProjectileMeshName = "ProjectileQuadMesh";
public const string AoeMeshName = "AoeQuadMesh";

// Effectively-infinite, cosmetic batch bounds (was CombatRoot.batchBoundsHalfExtent).
private const float BoundsHalfExtent = 100000f;
```

### Register (mint + build + publish)

```csharp
// The one render-resource registration entry point. Mints a render id, builds the
// GPU resources on the calling (main) thread, and publishes them into Entries under
// the batch id (== renderId). Projectiles and AOEs both register here, distinguished
// only by mesh name. Returns 0 for a null sprite (== "no visual").
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
    Entries[renderId] = new CombatRenderResourceEntry
    {
        Resources = resources,
        Layer = layer,
        BoundsHalfExtent = BoundsHalfExtent
    };
    return renderId;
}
```

No `faction` parameter (batch id is faction-free) and no `BatchIdFor` (apply systems
already write `cmd.RenderTypeId` into `CombatRenderBatchId.Value`).

### Render-component builders

These read the resource straight from `Entries`. The render-Z layout constants stay
in `CombatRoot` (external systems reference `CombatRoot.ProjectileRenderZ` etc.), so
the registry references them as consts.

```csharp
public CombatRenderComponent GetProjectileRenderComponent(int renderId, int projectileId)
{
    if (!Entries.TryGetValue(renderId, out var entry)) return default;
    CombatSpriteRenderResources res = entry.Resources;
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

// AOE visual data comes from the geometry, not the stored resource; the entry only
// gates whether a visual exists. The renderId is present in Entries iff the AOE type's
// prefab carried a sprite (see TryBuildAoeRenderResource / TryGetVisual in 002); a
// vfx-only / spriteless type has no entry and this returns default.
public CombatRenderComponent GetAoeRenderComponent(int renderId, AoeSpawnGeometry geometry)
{
    if (!Entries.ContainsKey(renderId)) return default;
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

### Teardown

```csharp
// Destroys all GPU resources and clears the store. One root owns all entries, so
// teardown clears everything. Called from CombatRoot.OnDestroy.
public void Unregister()
{
    foreach (var entry in Entries.Values)
        entry.Resources.Destroy();
    Entries.Clear();
    _nextRenderId = 1;
}
```

## New usings / namespace notes
- `Entries` already exists on the type; `CombatSpriteRenderResources`, `BatchedSpriteRenderer`,
  `AoeSpawnGeometry`, and `CombatRoot` are all in `PlayGround.System.*` — confirm the
  needed `using`s (`UnityEngine` for `Sprite`/`Material`/`Vector2`, `Unity.Mathematics`
  for `float2`) are present in `CombatRenderComponents.cs`.

## Acceptance Criteria
- Registry compiles standalone with `Register`, `GetProjectileRenderComponent`,
  `GetAoeRenderComponent`, `Unregister`, `ProjectileMeshName`, `AoeMeshName` as `public`.
- No `_resourcesById` (or any second resource dictionary) is introduced — `Entries` is
  the sole store.
- No callers wired yet — this step only adds API.

## Scope
Small. ~70 lines added to one file.

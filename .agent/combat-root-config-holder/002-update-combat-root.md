# 002 — Update CombatRoot

## File
`Assets/Scripts/System/Common/CombatRoot.cs`

## Dependency
Requires 001 (registry API must exist).

## What to Remove

Delete these fields:
```
renderResourcesById   Dictionary<int, CombatSpriteRenderResources>
nextRenderId          int
```

Delete these methods:
```
RegisterRenderResource(Sprite, Vector2, float, Material, string) → int
ProjectileRenderComponentForRenderId(int, int) → CombatRenderComponent
AoeRenderComponentForRenderId(int, AoeSpawnGeometry) → CombatRenderComponent
DestroyRenderResources()
BatchIdFor(CombatFaction, int) → int        (static; now lives on registry)
ProjectileMeshName / AoeMeshName            (consts; move inline or to registry)
```

## What to Add

Expose the registry reference:
```csharp
internal CombatRenderResourceRegistry RenderRegistry => _renderRegistry;
```

## What to Update

**Awake setup — `BuildProjectileRenderResources`**
Replace every call to `RegisterRenderResource(...)` with
`_renderRegistry.Register(..., faction, gameObject.layer)`.

No structural change — just the callee changes.

**Teardown — `OnDestroy`**
Replace:
```csharp
foreach (int renderId in renderResourcesById.Keys)
    _renderRegistry.Entries.Remove(BatchIdFor(faction, renderId));
_renderRegistry = null;
DestroyRenderResources();
```
With:
```csharp
_renderRegistry?.Unregister(faction);
_renderRegistry = null;
```

**AOE render resource — `TryBuildAoeRenderResource`**
Replace local `RegisterRenderResource(...)` call with `_renderRegistry.Register(..., faction, gameObject.layer)`.

**Command builders — `ProjectileCommandFor` and `AoeCommandFor`**
Replace `ProjectileRenderComponentForRenderId(renderId, id)` with
`_renderRegistry.GetProjectileRenderComponent(renderId, id)`.
Replace `AoeRenderComponentForRenderId(renderId, geometry)` with
`_renderRegistry.GetAoeRenderComponent(renderId, geometry)`.

**Template render helpers — `ProjectileTemplateRenderComponent`, `AoeTemplateRenderComponent`**
Replace with direct registry calls:
```csharp
internal CombatRenderComponent ProjectileTemplateRenderComponent(int renderId) =>
    _renderRegistry?.GetProjectileRenderComponent(renderId, 0) ?? default;

internal CombatRenderComponent AoeTemplateRenderComponent(int renderId, AoeSpawnGeometry geometry) =>
    _renderRegistry?.GetAoeRenderComponent(renderId, geometry) ?? default;
```

**Keep in CombatRoot (local maps still needed for `Spawn(Request)` path)**
```
projectileRenderIdByType   Dictionary<int, int>
aoeRenderIdByType          Dictionary<int, int>
ProjectileRenderId(int) → int
AoeRenderId(int) → int
```
These are needed by `ProjectileCommandFor` and `AoeCommandFor` to resolve
renderId from the behavior typeId when spawning from a managed `ProjectileSpawnRequest`/
`AoeSpawnRequest`. They contain no GPU state — just ints.

Also keep: `ProjectileVisualScale(float)` helper — pure math, used in setup.

## Acceptance Criteria
- `CombatRoot` no longer allocates or destroys any GPU objects directly.
- `CombatRoot.RenderRegistry` returns the cached `CombatRenderResourceRegistry`.
- Teardown calls `registry.Unregister(faction)` instead of the manual loop + `DestroyRenderResources`.
- `ProjectileRenderIdByType` / `AoeRenderIdByType` maps still populated correctly.
- No references to deleted fields/methods remain in this file.

## Scope
Medium. ~40 lines removed, ~15 changed. Structural change but no new concepts.

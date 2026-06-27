# 003 — Update CombatRoot

**File**: `Assets/Scripts/System/Common/CombatRoot.cs`
**Depends on**: 001, 002
**Scope**: medium — new helper, registry population on build/register/teardown, cached registry ref,
drop of two internal public properties

## What to do

### Add `BatchIdFor` helper

```csharp
private static int BatchIdFor(CombatFaction faction, int typeId) =>
    ((int)faction << 16) | typeId;
```

### Cache the registry reference

Add a field:
```csharp
private CombatRenderResourceRegistry _renderRegistry;
```

Populate it in `Awake`, after `BindWorld()` (which sets `entityManager`):

```csharp
using EntityQuery q = entityManager.CreateEntityQuery(
    ComponentType.ReadOnly<CombatRenderResourceRegistry>());
_renderRegistry = q.IsEmpty
    ? null
    : entityManager.GetComponentObject<CombatRenderResourceRegistry>(q.GetSingletonEntity());
```

If `_renderRegistry` is null (defensive — should not happen if step 002 lands first), log a warning
and skip registry population.

### Populate registry when building resources

In `BuildProjectileRenderResources`, after each `projectileRenderResourcesByType[typeId] = ...`,
add the corresponding entry:

```csharp
_renderRegistry?.Entries[BatchIdFor(faction, typeId)] = new CombatRenderResourceEntry
{
    Resources = projectileRenderResourcesByType[typeId],
    Layer = gameObject.layer,
    BoundsHalfExtent = batchBoundsHalfExtent
};
```

Do the same in `RegisterTemplate` (called lazily) and `TryBuildAoeRenderResource` (for AOE types).

### Remove registry entries on teardown

In `OnDestroy`, before calling `DestroyRenderResources()`:

```csharp
if (_renderRegistry != null)
{
    foreach (int typeId in projectileRenderResourcesByType.Keys)
        _renderRegistry.Entries.Remove(BatchIdFor(faction, typeId));
    foreach (int typeId in aoeRenderResourcesByType.Keys)
        _renderRegistry.Entries.Remove(BatchIdFor(faction, typeId));
    _renderRegistry = null;
}
DestroyRenderResources(); // unchanged — calls Destroy() on Mesh/Material
```

Remove-before-destroy order ensures the registry never holds references to destroyed objects.

### Drop the two exposed resource properties

`ProjectileRenderResources` and `AoeRenderResources` are `internal` properties read only by
`CombatBatchedRenderSystem`. After step 002 that system reads the singleton instead, so both
properties are dead:

```csharp
// Remove:
internal IReadOnlyDictionary<int, CombatSpriteRenderResources> ProjectileRenderResources => ...
internal IReadOnlyDictionary<int, CombatSpriteRenderResources> AoeRenderResources => ...
```

The private backing fields (`projectileRenderResourcesByType`, `aoeRenderResourcesByType`) remain —
they are still used by `AoeRenderComponentFor` and `ProjectileRenderComponentFor` to build the
per-entity `CombatRenderComponent` at spawn time.

## Notes

- `RenderLayer` and `BatchBoundsHalfExtent` properties remain on `CombatRoot` for now — they are
  read nowhere after step 002 removes the render-system usage, so they become dead code. Leave them
  for step 005 cleanup to keep this step focused.
- Accessing `entityManager.GetComponentObject<T>()` from a MonoBehaviour is valid on the main thread.
  The `CreateEntityQuery` call in `Awake` is a one-time cold path; dispose the query immediately
  after use (shown above with `using`).

## Acceptance

- `CombatRoot` no longer exposes `ProjectileRenderResources` or `AoeRenderResources`.
- Every resource built in `BuildProjectileRenderResources`, `RegisterTemplate`, and
  `TryBuildAoeRenderResource` has a matching entry added to the registry.
- `OnDestroy` removes all owned entries before calling `Destroy()` on the resources.
- `BatchIdFor` is the single point of batchId computation.

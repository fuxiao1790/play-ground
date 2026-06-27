# 002 — Rewrite CombatBatchedRenderSystem

**File**: `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`
**Depends on**: 001
**Scope**: medium — `OnCreate` grows a singleton creation; `OnUpdate` / `SubmitFaction` / `SubmitDomain`
replaced with a single registry-driven loop

## What to do

### `OnCreate`

Create the singleton entity carrying `CombatRenderResourceRegistry`:

```csharp
protected override void OnCreate()
{
    // existing query setup ...

    Entity registryEntity = EntityManager.CreateEntity();
    EntityManager.AddComponentObject(registryEntity, new CombatRenderResourceRegistry());

    // existing submitBuffer allocation ...
}
```

Drop the two per-domain query builds (`projectileRenderQuery`, `aoeRenderQuery`) and rebuild them to
filter by `CombatRenderBatchId` instead of the two old shared components:

```csharp
projectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
    .WithAll<CombatRenderElement>()
    .WithAll<CombatRenderBatchId>()
    .WithAll<CombatRenderActiveTag>()
    .WithAll<ProjectileTag>()
    .Build(this);

aoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
    .WithAll<CombatRenderElement>()
    .WithAll<CombatRenderBatchId>()
    .WithAll<CombatRenderActiveTag>()
    .WithAll<AoeTag>()
    .Build(this);
```

### `OnUpdate`

Replace the two `TryGetByFaction` calls with a single registry read:

```csharp
protected override void OnUpdate()
{
    CompleteDependency();

    var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
    foreach (KeyValuePair<int, CombatRenderResourceEntry> pair in registry.Entries)
    {
        SubmitBatchId(pair.Key, pair.Value);
    }
}
```

### `SubmitBatchId` (replaces `SubmitFaction` + `SubmitDomain`)

```csharp
private void SubmitBatchId(int batchId, CombatRenderResourceEntry entry)
{
    CombatRenderBatchId filter = new CombatRenderBatchId { Value = batchId };

    projectileRenderQuery.SetSharedComponentFilter(filter);
    using NativeArray<CombatRenderElement> projElements =
        projectileRenderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
    SubmitBatches(projElements, entry.Resources, entry.Layer, entry.BoundsHalfExtent);
    projectileRenderQuery.ResetFilter();

    aoeRenderQuery.SetSharedComponentFilter(filter);
    using NativeArray<CombatRenderElement> aoeElements =
        aoeRenderQuery.ToComponentDataArray<CombatRenderElement>(Allocator.Temp);
    SubmitBatches(aoeElements, entry.Resources, entry.Layer, entry.BoundsHalfExtent);
    aoeRenderQuery.ResetFilter();
}
```

`SubmitBatches` is unchanged.

### Removals

- Delete `SubmitFaction`, `SubmitDomain`.
- Remove the `using PlayGround.System.Aoe` / `using PlayGround.System.Projectile` imports if they
  were only needed by the old code (verify first).

## Notes

- `SystemAPI.ManagedAPI.GetSingleton<T>()` is the correct accessor for class-based `IComponentData`
  from within a `SystemBase`. It throws if no singleton exists — which is safe since `OnCreate`
  guarantees creation.
- At this point entities still carry `CombatRenderFaction`/`CombatRenderTypeId` and NOT yet
  `CombatRenderBatchId`, so the render system will submit zero elements per entry until step 004
  is complete. That is an acceptable mid-migration state (nothing renders for the affected entities,
  but there are no crashes and no data corruption).

## Acceptance

- Compiles alongside old shared components (no removals yet).
- `CombatBatchedRenderSystem` imports no reference to `CombatRoot`, `CombatFaction`, or
  `TryGetByFaction`.
- `SubmitBatchId` iterates registry entries, not MonoBehaviour roots.

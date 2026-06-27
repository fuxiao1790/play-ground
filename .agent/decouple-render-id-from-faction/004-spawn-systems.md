# 004 — Update Spawn Systems

**Files**: `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`,
`Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
**Depends on**: 001
**Scope**: medium — dead-slot query filters, cold-create shared component stamps, spawn key structs

After this step entities carry `CombatRenderBatchId` and the render system (step 002) produces
correct output. This is the step that closes the mid-migration rendering gap.

---

## AoeSpawnApplySystem

### Compute batchId

Wherever `faction` and `key.TypeId` are both in scope, compute:
```csharp
int batchId = ((int)faction << 16) | key.TypeId;
```

### `DeadSlotQueryFor` — replace double filter with single

Current (three query variants all have the same pattern):
```csharp
.WithAll<CombatRenderFaction>()
.WithAll<CombatRenderTypeId>()
```
Replace with:
```csharp
.WithAll<CombatRenderBatchId>()
```

Current filter call:
```csharp
query.SetSharedComponentFilter(
    new CombatRenderFaction { Faction = faction },
    new CombatRenderTypeId { TypeId = key.TypeId });
```
Replace with:
```csharp
query.SetSharedComponentFilter(new CombatRenderBatchId { Value = batchId });
```

### `CreateAoeEntity` — one `AddSharedComponent` instead of two

Current:
```csharp
ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
```
Replace with:
```csharp
ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = ((int)faction << 16) | cmd.TypeId });
```

### `AoeSpawnKey` — collapse `_factionValue` + `_typeId` into `_batchId`

```csharp
private readonly struct AoeSpawnKey : IEquatable<AoeSpawnKey>
{
    private readonly int _batchId;
    private readonly bool _lingering;
    private readonly bool _hasTimedSpawner;

    public int BatchId           => _batchId;
    public bool Lingering        => _lingering;
    public bool HasTimedSpawner  => _hasTimedSpawner;

    public AoeSpawnKey(int batchId, bool lingering, bool hasTimedSpawner)
    {
        _batchId         = batchId;
        _lingering       = lingering;
        _hasTimedSpawner = hasTimedSpawner;
    }

    public bool Equals(AoeSpawnKey other) =>
        _batchId == other._batchId
        && _lingering == other._lingering
        && _hasTimedSpawner == other._hasTimedSpawner;

    public override bool Equals(object obj) => obj is AoeSpawnKey k && Equals(k);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = _batchId;
            hash = hash * 397 ^ (_lingering ? 1 : 0);
            hash = hash * 397 ^ (_hasTimedSpawner ? 1 : 0);
            return hash;
        }
    }
}
```

Update all `AoeSpawnKey` construction sites to pass `batchId` instead of `(factionValue, typeId)`.

---

## ProjectileSpawnApplySystem

### `DeadSlotQueryFor` — replace double filter with single

Same pattern as AOE. In `BuildDeadSlotQuery` overrides in both
`BasicProjectileSpawnApplySystem` and `ChildSpawnerProjectileSpawnApplySystem`:

```csharp
// Before
.WithAll<CombatRenderFaction>()
.WithAll<CombatRenderTypeId>()
// After
.WithAll<CombatRenderBatchId>()
```

Filter call in `DeadSlotQueryFor`:
```csharp
// Before
query.SetSharedComponentFilter(
    new CombatRenderFaction { Faction = faction },
    new CombatRenderTypeId { TypeId = key.TypeId });
// After
query.SetSharedComponentFilter(new CombatRenderBatchId { Value = key.BatchId });
```

### `CreateProjectileEntity` — both subclasses

Both `BasicProjectileSpawnApplySystem.CreateProjectileEntity` and
`ChildSpawnerProjectileSpawnApplySystem.CreateProjectileEntity`:

```csharp
// Before
ecb.AddSharedComponent(entity, new CombatRenderFaction { Faction = faction });
ecb.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = cmd.TypeId });
// After
ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = ((int)faction << 16) | cmd.TypeId });
```

### `ProjectileSpawnKey` — collapse `_factionValue` + `_typeId` into `_batchId`

```csharp
private readonly struct ProjectileSpawnKey : IEquatable<ProjectileSpawnKey>
{
    private readonly int _batchId;

    public int BatchId => _batchId;

    public ProjectileSpawnKey(int batchId) { _batchId = batchId; }

    public bool Equals(ProjectileSpawnKey other) => _batchId == other._batchId;
    public override bool Equals(object obj) => obj is ProjectileSpawnKey k && Equals(k);
    public override int GetHashCode() => _batchId;
}
```

Update all `ProjectileSpawnKey` construction sites. The old constructor took `(factionValue, typeId)`;
the new one takes `batchId = ((int)faction << 16) | typeId`.

---

## Acceptance

- All three `AoeSpawnApplySystem` dead-slot query variants filter by `CombatRenderBatchId` only.
- Both `BasicProjectileSpawnApplySystem` and `ChildSpawnerProjectileSpawnApplySystem` filter and
  cold-create with `CombatRenderBatchId` only.
- Newly spawned entities carry `CombatRenderBatchId`; no entity carries both old and new shared
  components after this step.
- Spawn reuse works: a dead-slot entity's `CombatRenderBatchId` matches the inbound command's batchId
  and the job resets it correctly (batchId is a shared component, not reset per-entity in the job).
- Render system (step 002) now finds entities and submits correctly — the end-to-end rendering gap
  is closed.

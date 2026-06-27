# 005 — Delete Old Types and Update Tests

**Files**: `Assets/Scripts/System/Common/CombatRenderComponents.cs`,
`Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`,
`Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
**Depends on**: 001, 002, 003, 004
**Scope**: small — deletions and test updates only; no new logic

Do not start this step until 002–004 are verified working end-to-end. These are pure removals.

---

## `CombatRenderComponents.cs`

Delete the two old shared component structs in their entirety:

```csharp
// Delete:
public struct CombatRenderFaction : ISharedComponentData, ...
public struct CombatRenderTypeId : ISharedComponentData, ...
```

Also remove any dead `CombatRoot` properties that remained from step 003:
- `internal int RenderLayer => gameObject.layer;`
- `internal float BatchBoundsHalfExtent => batchBoundsHalfExtent;`

(Verify these are unreferenced before deleting.)

---

## `ProjectileSpawnPipelineTests.cs` — lines 408–409

Replace construction of the two old components with a single `CombatRenderBatchId`:

```csharp
// Before:
entityManager.AddSharedComponent(entity, new CombatRenderFaction { Faction = CombatFaction.Player });
entityManager.AddSharedComponent(entity, new CombatRenderTypeId { TypeId = 1 });

// After:
entityManager.AddSharedComponent(entity, new CombatRenderBatchId { Value = ((int)CombatFaction.Player << 16) | 1 });
```

---

## `BareMinimumPrototypePlayModeTests.cs` — lines 904–910

The test builds a query and sets a shared component filter. Replace:

```csharp
// Before:
ComponentType.ReadOnly<CombatRenderFaction>(),
ComponentType.ReadOnly<CombatRenderTypeId>(),
// ...
new CombatRenderFaction { Faction = faction },
new CombatRenderTypeId { TypeId = typeId });

// After:
ComponentType.ReadOnly<CombatRenderBatchId>(),
// ...
new CombatRenderBatchId { Value = ((int)faction << 16) | typeId });
```

---

## Acceptance

- Project compiles with zero references to `CombatRenderFaction` or `CombatRenderTypeId`.
- All play-mode tests pass.
- `CombatRenderFaction` does not appear anywhere in the codebase.

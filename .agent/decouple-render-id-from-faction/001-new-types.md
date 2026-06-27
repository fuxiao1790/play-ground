# 001 — New Types

**File**: `Assets/Scripts/System/Common/CombatRenderComponents.cs`
**Depends on**: nothing
**Scope**: small — three type definitions, no logic changes

## What to do

Add three new types to `CombatRenderComponents.cs`. Do not remove old types yet (that is step 005).

### 1. `CombatRenderBatchId`

```csharp
// ECS Lifecycle: shared render component; added at entity creation; kept until owning domain root
// teardown; partitions render chunks by globally unique batch id without structural archetype cost.
public struct CombatRenderBatchId : ISharedComponentData, System.IEquatable<CombatRenderBatchId>
{
    public int Value;
    public readonly bool Equals(CombatRenderBatchId other) => Value == other.Value;
    public override int GetHashCode() => Value;
}
```

### 2. `CombatRenderResourceEntry`

A plain record (or readonly struct) — no ECS interface; lives inside the registry dictionary.

```csharp
public sealed class CombatRenderResourceEntry
{
    public CombatSpriteRenderResources Resources;
    public int Layer;
    public float BoundsHalfExtent;
}
```

### 3. `CombatRenderResourceRegistry`

Managed `IComponentData` — must be a class, not a struct, because `Dictionary` is a managed type.

```csharp
public sealed class CombatRenderResourceRegistry : IComponentData
{
    public readonly Dictionary<int, CombatRenderResourceEntry> Entries = new();
}
```

## Acceptance

- All three types compile alongside the existing `CombatRenderFaction` and `CombatRenderTypeId` (no
  removals yet).
- No other file is touched in this step.

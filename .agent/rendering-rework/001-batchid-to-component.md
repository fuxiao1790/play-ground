# 001 — Convert CombatRenderBatchId to IComponentData

## Goal
Change `CombatRenderBatchId` from `ISharedComponentData` to a plain
`IComponentData` int. This is the root change; everything else adapts to it.

## Change
[CombatRenderComponents.cs:35](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L35)

```csharp
// before: shared component, requires IEquatable + GetHashCode
public struct CombatRenderBatchId : ISharedComponentData, IEquatable<CombatRenderBatchId> { ... }

// after: plain per-entity data
public struct CombatRenderBatchId : IComponentData
{
    public int Value;
}
```

- Drop the `IEquatable`/`Equals`/`GetHashCode` boilerplate — only shared
  components need it for chunk keying.
- Update the doc comment: it is no longer "partitions render chunks by globally
  unique batch id"; it is now "per-entity render/resource id copied from the spawn
  command's `RenderTypeId`; identifies the GPU resource batch at submit."

## Acceptance criteria
- Type compiles as `IComponentData`.
- No remaining reference assumes it is shared (verified by 002–005 landing).

## Notes
- This alone breaks compilation of the spawn systems and render system; those are
  fixed in 002–004. Land as one unit.

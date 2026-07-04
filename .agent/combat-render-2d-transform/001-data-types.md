# 001 — Data types: compact render component + authoring split

## Scope
`Assets/Scripts/System/Common/CombatRenderComponents.cs` (struct region only;
registry `Get*` is task 003, matrix utility is task 002).

## Change
Redefine `CombatRenderComponent` as the 32 B GPU wire record and add a CPU-only
`CombatRenderAuthoring` holding what used to live in the matrix's unused cells.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CombatRenderComponent : IComponentData
{
    public float4 Rotation;  // objectToWorld m00, m01, m10, m11 (2x2 rotation*scale)
    public float3 Position;  // world x, world y, RenderZ
    public int RenderMeta;   // render id bits 0..30; align-to-velocity bit 31
    // Accessors kept (back onto the new fields):
    //   RenderZ            -> Position.z
    //   RenderMeta-derived -> AlignToVelocity, RenderTypeId, IsRenderable (unchanged bit logic)
}

public struct CombatRenderAuthoring : IComponentData
{
    public float2 BaseScale;  // authored kind scale * (AOE geometry scale)
    public float BaseSin;     // authored VisualRotationSin
    public float BaseCos;     // authored VisualRotationCos
}
```

## Accessors
- **Keep on `CombatRenderComponent`** (call sites use them as properties —
  `render.RenderZ = ...` in ProjectileSpawnExpansionSystem:204, CombatRoot:513;
  `AlignToVelocity`, `RenderTypeId`, `IsRenderable`): reimplement `RenderZ` as
  `Position.z`; `AlignToVelocity`/`RenderTypeId`/`IsRenderable` keep their exact
  bit logic on `RenderMeta`.
- **Move to `CombatRenderAuthoring`**: `VisualScale`, `VisualRotationSin`,
  `VisualRotationCos`, `SetVisualTransform`, `SetVisual2D`. `SetVisualTransform`'s
  `renderZ`/`alignToVelocity`/`renderTypeId` params still target the *render*
  component (they are GPU/meta fields), so the registry (task 003) sets those on
  the render component and scale/sin/cos on the authoring — do not try to keep a
  combined setter that spans both components.

## Acceptance
- `UnsafeUtility.SizeOf<CombatRenderComponent>() == 32`,
  `SizeOf<CombatRenderAuthoring>() == 16`.
- No member of `CombatRenderComponent` crosses a 16-byte boundary (rotation
  0..15, position 16..27, meta 28..31).
- Compiles only after 002-004 (references elsewhere break until then — expected).

## Notes
- `math.max(scale.x, scale.y)` (old `m22`) is dropped — it was never a GPU input
  (quad local z = 0) and nothing reads it back.
- `using Unity.Mathematics;` already present.

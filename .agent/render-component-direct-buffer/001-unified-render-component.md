---
task: 001-unified-render-component
title: Merge CombatRenderComponent and CombatRenderElement into unified GPU-shaped struct
---

## Summary

Merge `CombatRenderComponent` (UV, visual parameters) and `CombatRenderElement` (matrix) into a single struct `CombatRenderComponent` that exactly matches the GPU buffer layout, with renderTypeId stored in the uvV.z field (currently unused by shader).

## Acceptance Criteria

- [ ] New `CombatRenderComponent` struct is **exactly 96 bytes** (asserted in prep system OnCreate)
- [ ] Structure: `Matrix4x4 objectToWorld` (64) + `Vector4 uvOriginU` (16) + `Vector4 uvV` (16), with renderTypeId accessible via uvV.z
- [ ] All existing fields preserved and accessible (VisualScale, rotation, RenderZ from objectToWorld; UV basis from vectors)
- [ ] `CombatInstanceData` removed (no longer needed; component IS the GPU data)
- [ ] `CombatRenderElement` removed (merged into CombatRenderComponent)
- [ ] Spawn-time component creation (`GetProjectileRenderComponent`, `GetAoeRenderComponent`) updated to populate the new struct

## Changes

### File: `Assets/Scripts/System/Common/CombatRenderComponents.cs`

1. **Remove** `CombatRenderElement` struct entirely
2. **Remove** `CombatInstanceData` struct entirely
3. **Replace** `CombatRenderComponent` with unified struct (exactly GPU layout):
   ```csharp
   [StructLayout(LayoutKind.Sequential)]
   public struct CombatRenderComponent : IComponentData
   {
       // GPU data (exactly 96 bytes; sent to StructuredBuffer)
       public Matrix4x4 objectToWorld;  // 64 bytes (written by prep system each frame)
       public Vector4 uvOriginU;        // 16 bytes (xy=origin, zw=U axis; from spawn-time registry)
       public Vector4 uvV;              // 16 bytes (xy=V axis; zw unused by shader)
   }
   ```
   
4. **Update spawn-time component builders** to match:
   - `GetProjectileRenderComponent(...)` → returns CombatRenderComponent with objectToWorld = default, uvOriginU/uvV populated
   - `GetAoeRenderComponent(...)` → returns CombatRenderComponent with objectToWorld = default, uvOriginU/uvV populated

5. **Add struct-size assertion** in `CombatRenderPrepareSystem.OnCreate()`:
   ```csharp
   Assert.AreEqual(96, UnsafeUtility.SizeOf<CombatRenderComponent>());
   ```

## Notes

- The objectToWorld matrix will be **written every frame** by RenderPrepareSystem (currently CombatRenderMatrixUtility.ElementFor)
- The uvOriginU / uvV / renderTypeId will be **written once at spawn**, then updated/maintained by other systems
- No changes needed to spawn command building or apply plumbing—they already write CombatRenderComponent; just the struct shape changes

## Dependencies

- None (prep, batched, and shader changes depend on this)

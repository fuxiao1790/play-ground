---
task: 002-prep-system-all-entities
title: Modify CombatRenderPrepareSystem to write all entities (enabled and disabled)
---

## Summary

Change `CombatRenderPrepareSystem` to iterate **all** entities with render components (enabled and disabled). For enabled entities, write the normal transform matrix. For disabled entities, write a degenerate matrix with scale=0 (results in a 0×0 quad that the shader naturally culls).

## Acceptance Criteria

- [ ] Prep query no longer filters by `CombatRenderActiveTag` enabled state
- [ ] Job iterates all entities in chunk (enabled and disabled via useEnabledMask)
- [ ] For enabled entities: write objectToWorld matrix normally (via CombatRenderMatrixUtility.ElementFor)
- [ ] For disabled entities: write a degenerate objectToWorld with scale=0 (0×0 quad)
- [ ] Matrix computation unchanged for enabled entities (CombatRenderMatrixUtility.ElementFor still used)

## Changes

### File: `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

1. **Update OnCreate**: Modify the query to include CombatRenderActiveTag but not filter by it:
   ```csharp
   renderPrepareQuery = new EntityQueryBuilder(Allocator.Temp)
       .WithAll<CombatKinematicsComponent>()
       .WithAll<CombatRenderComponent>()
       .WithAll<CombatRenderActiveTag>()  // Present, but not filtered
       .Build(this);
   ```
   (The tag is still required; just don't use `.Build(this)` options to disable the enabled filter. Actually, check if there's an `.IgnoreEnabled()` call or similar—if the query is built with just WithAll, it auto-filters enabled. May need to add a second query or use different strategy.)

   **Alternative**: Keep the current query as-is (filters enabled), but then add a second job that writes disabled entities with renderTypeId=0. This is more localized but adds complexity.

   **Recommended approach**: Use `RequireForUpdate(false)` or iterate all entities explicitly without the enableable filter. Read current EntityQueryBuilder API to see the correct method.

2. **Update RenderPrepareJob** to handle enabled/disabled:
   ```csharp
   [BurstCompile]
   private struct RenderPrepareJob : IJobChunk
   {
       [ReadOnly] public ComponentTypeHandle<CombatKinematicsComponent> Kinematics;
       [ReadOnly] public ComponentTypeHandle<CombatRenderComponent> RenderComponents;
       public ComponentTypeHandle<CombatRenderComponent> RenderComponents;  // Write access

       public void Execute(
           in ArchetypeChunk chunk,
           int unfilteredChunkIndex,
           bool useEnabledMask,
           in v128 chunkEnabledMask)
       {
           var kin = chunk.GetNativeArray(ref Kinematics);
           var rend = chunk.GetNativeArray(ref RenderComponents);

           var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
           while (enumerator.NextEntityIndex(out int i))
           {
               // Enabled: write normal matrix
               var component = rend[i];
               component.objectToWorld = CombatRenderMatrixUtility.ElementFor(kin[i], rend[i]);
               rend[i] = component;
           }

           // Disabled entities: write degenerate matrix (scale=0)
           if (useEnabledMask)
           {
               var maskEnumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count, inverted: true);
               while (maskEnumerator.NextEntityIndex(out int i))
               {
                   var component = rend[i];
                   // Zero out the scale columns of the matrix (m00, m01, m10, m11, m20, m21)
                   // This results in a 0×0 quad
                   var m = component.objectToWorld;
                   m.m00 = 0f;
                   m.m01 = 0f;
                   m.m10 = 0f;
                   m.m11 = 0f;
                   m.m20 = 0f;
                   m.m21 = 0f;
                   component.objectToWorld = m;
                   rend[i] = component;
               }
           }
       }
   }
   ```

## Notes

- Disabled entities: zeroing the scale columns (m00, m01, m10, m11, m20, m21) results in a 0×0 quad that GPU naturally culls (no special shader handling needed)
- Position (m03, m13) is preserved; scale and rotation are zeroed
- This is efficient: no renderTypeId field needed, shader unchanged, culling happens naturally

## Dependencies

- Depends on 001 (unified component struct exists)
- Required by 003 (batched system depends on all entities being written)

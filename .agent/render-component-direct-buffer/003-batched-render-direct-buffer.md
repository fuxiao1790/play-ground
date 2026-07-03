---
task: 003-batched-render-direct-buffer
title: Replace scatter + SetData with direct LockForWrite buffer write
---

## Summary

Replace the scatter loop (filter + NativeList build) and SetData copy with a direct buffer lock. Lock the GPU buffer, iterate entities sequentially, write component data directly, and unlock. Eliminates the intermediate NativeList and the copy overhead.

## Acceptance Criteria

- [ ] No scatter loop; no filtering via `activeMask[i]`
- [ ] No `NativeList<CombatInstanceData>` allocated or used
- [ ] Buffer locked via `LockBufferForWrite<CombatRenderComponent>` before writing
- [ ] Components written sequentially to locked buffer pointer
- [ ] Buffer unlocked via `UnlockBufferAfterWrite<CombatRenderComponent>` after writing
- [ ] `CombatInstanceData` struct removed entirely (was only a GPU upload intermediate)
- [ ] Instance capacity logic preserved (grow by doubling as needed)
- [ ] Both projectile and AOE entities written in a single pass (unified query, not separate)
- [ ] All entities written to buffer (enabled and disabled; disabled have scale=0 and are GPU-culled)

## Changes

### File: `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

1. **Remove fields**:
   - `private NativeList<CombatInstanceData> _instances;`

2. **Update OnCreate**:
   - Remove the separate projectileRenderQuery and aoeRenderQuery
   - Add a single unified query:
     ```csharp
     renderQuery = new EntityQueryBuilder(Allocator.Temp)
         .WithAll<CombatRenderElement>()  // Old name; now it's part of CombatRenderComponent
         .WithAll<CombatRenderComponent>()
         .WithAny<ProjectileTag, AoeTag>()  // Both kinds
         .Build(this);
     ```
   - Remove the assertion for `CombatInstanceData` size (moved to prep system)
   - Add assertion for `CombatRenderComponent` size: `Assert.AreEqual(96, UnsafeUtility.SizeOf<CombatRenderComponent>());`

3. **Update OnDestroy**:
   - Remove `_instances` disposal
   - Keep buffer disposal unchanged

4. **Replace OnUpdate**:
   ```csharp
   protected override void OnUpdate()
   {
       CompleteDependency();

       var registry = SystemAPI.ManagedAPI.GetSingleton<CombatRenderResourceRegistry>();
       if (registry.SharedMesh == null)
       {
           CombatIndirectRenderData.Clear();
           return;
       }

       int entityCount = renderQuery.CalculateEntityCount();
       if (entityCount == 0)
       {
           CombatIndirectRenderData.Clear();
           return;
       }

       EnsureInstanceCapacity(entityCount);

       // Lock buffer and write directly
       var ptr = (CombatRenderComponent*)_instanceBuffer.LockBufferForWrite<CombatRenderComponent>(
           0, entityCount);

       WriteDirect(renderQuery, ptr);

       _instanceBuffer.UnlockBufferAfterWrite<CombatRenderComponent>(entityCount);

       // Update args for the instance count
       PopulateArgs(registry, entityCount);
       Submit(registry);

       // (Optional) Count active entities for telemetry
       CountActiveForTelemetry(renderQuery);
   }
   ```

5. **New WriteDirect method**:
   ```csharp
   private void WriteDirect(EntityQuery query, CombatRenderComponent* ptr)
   {
       int writeIndex = 0;
       using NativeArray<ArchetypeChunk> chunks = query.ToArchetypeChunkArray(Allocator.Temp);
       foreach (ArchetypeChunk chunk in chunks)
       {
           NativeArray<CombatRenderComponent> components = chunk.GetNativeArray(ref renderComponentHandle);
           for (int i = 0; i < chunk.Count; i++)
           {
               ptr[writeIndex++] = components[i];
           }
       }
   }
   ```

6. **Simplify OnUpdate** (remove telemetry counting if not needed, or count directly from render components):
   ```csharp
   // If telemetry is needed: count from render-active entities before submit
   // For now: telemetry counts can be removed or counted separately (not in critical path)
   ```

7. **Update field declarations**:
   - Add: `private EntityQuery renderQuery;`
   - Add: `private ComponentTypeHandle<CombatRenderComponent> renderComponentHandle;`
   - Add: `private ComponentTypeHandle<ProjectileTag> projectileTagHandle;` (if needed for telemetry)
   - Add: `private ComponentTypeHandle<AoeTag> aoeTagHandle;` (if needed for telemetry)
   - Remove: `private EntityQuery projectileRenderQuery;`, `aoeRenderQuery`, and associated handles

8. **Simplify Scatter or remove it entirely**:
   - The old `Scatter` method is no longer used; delete it

## Notes

- `LockBufferForWrite` returns a pointer to the buffer's unmanaged data; must be unlocked after writing
- Component data written directly; no intermediate struct or conversion
- Buffer grows by doubling in `EnsureInstanceCapacity` (logic unchanged)
- `PopulateArgs` and `Submit` remain the same

## Testing

- [ ] Verify frame rate and GPU command buffer size (expect no regression or improvement)
- [ ] Verify all combat sprites render (no missing entities)
- [ ] Verify disabled entities don't render (renderTypeId=0 filtering in shader)
- [ ] Verify telemetry counts match expected active entities

## Dependencies

- Depends on 001 (unified component exists)
- Depends on 002 (all entities are pre-written by prep system with renderTypeId set)
- Required by 004 (shader must check renderTypeId)
- Required by 005 (unified query replaces separate scatter)

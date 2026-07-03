---
task: 005-unify-projectile-aoe-query
title: Use single unified query for projectile and AOE rendering (not separate)
---

## Summary

Replace the separate `projectileRenderQuery` and `aoeRenderQuery` with a single query that selects both entity kinds via `WithAny<ProjectileTag, AoeTag>()`. This eliminates artificial bifurcation in the render path and aligns with the principle that "rendering a sprite is just rendering a sprite."

## Acceptance Criteria

- [ ] Single EntityQuery that matches both projectile and AOE entities
- [ ] Both kinds written to the same GPU buffer in one pass
- [ ] `LastActiveProjectileCount` and `LastActiveAoeCount` still tracked separately (for telemetry/debugging)
- [ ] No change to render visual output or performance

## Changes

### File: `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

Already covered in task 003, but explicitly documented here:

1. **Replace separate queries**:
   ```csharp
   // OLD:
   projectileRenderQuery = new EntityQueryBuilder(Allocator.Temp)
       .WithAll<CombatRenderElement>()
       .WithAll<CombatRenderComponent>()
       .WithAll<CombatRenderActiveTag>()
       .WithAll<ProjectileTag>()
       .Build(this);

   aoeRenderQuery = new EntityQueryBuilder(Allocator.Temp)
       .WithAll<CombatRenderElement>()
       .WithAll<CombatRenderComponent>()
       .WithAll<CombatRenderActiveTag>()
       .WithAll<AoeTag>()
       .Build(this);

   // NEW:
   renderQuery = new EntityQueryBuilder(Allocator.Temp)
       .WithAll<CombatRenderComponent>()
       .WithAny<ProjectileTag, AoeTag>()
       .Build(this);
   ```

2. **Update field declarations**:
   - Remove: `private EntityQuery projectileRenderQuery;`, `aoeRenderQuery;`
   - Add: `private EntityQuery renderQuery;`

3. **Update OnCreate field handles**:
   ```csharp
   // For counting, if needed:
   projectileTagHandle = GetComponentTypeHandle<ProjectileTag>(true);
   aoeTagHandle = GetComponentTypeHandle<AoeTag>(true);
   ```

4. **Optional: Count separate kinds if telemetry is needed**:
   The `LastActiveProjectileCount` and `LastActiveAoeCount` fields can be kept for telemetry, but counting them is optional since disabled entities are now naturally culled (not filtered during upload). If counts are still desired, count from enabled entities via a separate pass after `CompleteDependency`.

## Rationale

- Projectiles and AOEs are rendered the same way (via the same shader, same buffer, same indirect call)
- The separate queries were an implementation detail for tracking; rendering itself has no logical distinction
- Unified query simplifies the data flow and removes artificial branching

## Notes

- Both tags can exist on the same entity (unlikely, but the query handles it correctly via `WithAny`)
- Telemetry counts must still distinguish them for monitoring; this is a post-render concern, not a render-path concern

## Dependencies

- Depends on 003 (which introduces the unified query pattern)
- No other systems depend on the separate projectile/AOE render queries

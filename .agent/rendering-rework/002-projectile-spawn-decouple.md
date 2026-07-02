# 002 — Projectile spawn: write batch id, drop shared partitioning

Applies to both `BasicProjectileSpawnApplySystem` and
`ChildSpawnerProjectileSpawnApplySystem` (shared base
`ProjectileSpawnApplySystemBase`).

## Changes

### 1. Archetypes — add the component
Add `typeof(CombatRenderBatchId)` to both `CreateArchetype()` builders
([:398](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L398), [:580](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L580)). It is now a normal component that lives on the entity from
creation.

### 2. Cold-create path (ECB) — SetComponent instead of AddSharedComponent
In `CreateProjectileEntity` ([:456](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L456), [:642](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L642)):
```csharp
// before
ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
// after — component is in the archetype; no structural op
ecb.SetComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
```
(Or fold the write into `RecordCommonProjectileReset`; keep it in one place.)

### 3. Reuse jobs — WRITE the batch id (new)
`BasicProjectileSpawnJob` / `ChildSpawnerProjectileSpawnJob` currently never set
batch id because the shared filter guaranteed the claimed slot already matched.
Now they must write it:
- Add `[NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderBatchId> RenderBatchIdHandle;`
- Populate it in `ScheduleReuseJob` ([:438-447](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L438-L447)).
- In `Execute`, alongside `renders[i] = cfg.Render;` ([:541](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L541)):
  ```csharp
  batchIds[i] = new CombatRenderBatchId { Value = cfg.RenderTypeId };
  ```
  where `batchIds = chunk.GetNativeArray(ref RenderBatchIdHandle);`

### 4. Dead-slot query — drop the shared filter
In `DeadSlotQueryFor` ([:205-215](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L205-L215)) delete:
```csharp
query.SetSharedComponentFilter(new CombatRenderBatchId { Value = key.BatchId });
```
The query now returns all dead slots of the archetype (basic: `WithNone<TimedSpawnTag>`;
child: `WithAll<TimedSpawnTag>`). `WithAll<CombatRenderBatchId>` in
`BuildDeadSlotQuery` ([:421](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L421), [:605](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L605)) may stay (harmless — every renderable has it) or
be removed; prefer removing for clarity.

### 5. Reuse key — drop BatchId (degenerate, keep machinery)
`ProjectileSpawnKey` ([:294](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L294)) reduces to no distinguishing field. Keep the
counting-sort but make it produce a **single bucket** (e.g. a fieldless/constant
key in `BucketCommandsJob` [:332](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L332)). Do NOT remove the bucketing this pass.

> Deferred cleanup (Follow-up 3 in index): with batch id gone from the key, the
> counting-sort no longer sorts anything for projectiles and can be removed
> entirely. Left in place now to keep this change minimal on the spawn path.

## Acceptance criteria
- Projectiles spawn (cold + reuse) and render with correct sprites after the
  render rewrite (004).
- A reused slot that previously held a different render type now renders with the
  new type (cross-batch reuse works).
- No `SetSharedComponentFilter` / `AddSharedComponent` for `CombatRenderBatchId`
  remains in projectile code.

## Depends on
001. Compiles together with 003, 004.

# 003 — AOE spawn: write batch id, drop shared partitioning

Applies to `AoeSpawnApplySystem` (three archetypes: impact, lingering,
timed-lingering).

## Changes

### 1. Archetypes — add the component
Add `typeof(CombatRenderBatchId)` to all three archetype builders
([:45-94](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L45-L94)).

### 2. Cold-create path (ECB)
In `CreateAoeEntity` ([:310](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L310)):
```csharp
// before
ecb.AddSharedComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
// after
ecb.SetComponent(entity, new CombatRenderBatchId { Value = cmd.RenderTypeId });
```
(Or fold into `RecordAoeReset`.)

### 3. Reuse job — WRITE the batch id (new)
`AoeSpawnJob` ([:412](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L412)) currently never writes batch id. Add:
- `[NativeDisableContainerSafetyRestriction] public ComponentTypeHandle<CombatRenderBatchId> RenderBatchIdHandle;`
- Populate in the job construction ([:166-179](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L166-L179)).
- In `Execute`, next to `renders[i] = render;` ([:524](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L524)):
  ```csharp
  batchIds[i] = new CombatRenderBatchId { Value = cfg.RenderTypeId };
  ```

### 4. Dead-slot queries — drop the shared filter
Delete the `SetSharedComponentFilter` line ([:288](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L288)). The three query variants
([:256-283](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L256-L283)) keep their lifetime/timed distinctions, which is what preserves the
impact/lingering/timed-lingering separation. `WithAll<CombatRenderBatchId>` may
stay or be removed.

### 5. Reuse key — drop BatchId only
`AoeSpawnKey` ([:538](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L538)) drops `_batchId`, keeps `_lingering` and
`_hasTimedSpawner`. Update the constructor call ([:128](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L128)) and `GetHashCode`/`Equals`.
The `_byKey` bucketing stays meaningful (3 buckets max).

## Acceptance criteria
- Impact, lingering, and timed-lingering AOEs spawn (cold + reuse) and render.
- Cross-batch reuse within an archetype variant renders the new type.
- No `SetSharedComponentFilter` / `AddSharedComponent` for `CombatRenderBatchId`
  remains in AOE code.

## Depends on
001. Compiles together with 002, 004.

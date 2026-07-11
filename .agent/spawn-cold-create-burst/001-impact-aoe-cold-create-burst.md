# 001 — Impact AOE cold-create into Burst job

**File:** [Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)
(`ImpactAoeSpawnApplySystem` + `ImpactAoeSpawnJob`)

## Change

1. **ECB allocator** (line ~90): `new EntityCommandBuffer(Allocator.Temp)` →
   `new EntityCommandBuffer(Allocator.TempJob)`. (`using var` dispose unchanged.)

2. **Add two fields to `ImpactAoeSpawnJob`:**
   ```csharp
   public EntityCommandBuffer Ecb;
   public EntityArchetype Archetype;
   ```

3. **Append the cold suffix at the tail of `ImpactAoeSpawnJob.Execute()`**, after
   the chunk loop and before `ReuseCount.Value = commandIndex;`:
   ```csharp
   for (int i = commandIndex; i < Configs.Length; i++)
   {
       Entity entity = Ecb.CreateEntity(Archetype);
       AoeSpawnApplyUtility.RecordImpactReset(Ecb, entity, Configs[i]);
   }

   ReuseCount.Value = commandIndex; // commandIndex unchanged by the loop above
   ```
   `commandIndex` is the reuse count coming out of the chunk loop; the loop uses
   its own `i`, so `ReuseCount.Value` still records reuse correctly.

4. **In `OnUpdate`**, pass the new fields when constructing the job and drop the
   main-thread cold loop (current lines ~120–126):
   ```csharp
   new ImpactAoeSpawnJob
   {
       Configs = commands,
       Chunks = chunks,
       ReuseCount = reused,
       Ecb = createEcb,
       Archetype = _impactArchetype,
       // ... existing handles unchanged ...
   }.Schedule(default).Complete();

   reuseCount = reused.Value;
   // (delete the `for (int i = reuseCount; i < commands.Length; i++)` recording loop)
   ```

5. **Cold count / playback** (unchanged in behavior, recompute from counts):
   ```csharp
   int coldCreateCount = commands.Length - reuseCount;
   ...
   if (coldCreateCount > 0)
   {
       createEcb.Playback(EntityManager);
   }
   ```
   The `ReuseJobMarker` scope now legitimately wraps reuse + cold recording (all
   Burst) + schedule/complete. Playback stays outside it, as today.

## Acceptance criteria

- Compiles; `ImpactAoeSpawnJob` Burst-compiles with no Burst errors
  (`RecordImpactReset` and its callees compile under Burst).
- `ImpactAoeSpawnApplySystem.Reuse` + `.Cold` still sum to the tick command
  count; a cold-only start frame reports `Reuse == 0`, `Cold == totalRequests`.
- `AoeSimulationTests` PlayMode suite passes (impact-AOE spawn/collision).
- Profiler: the impact spawn cost is Burst self-time (job), not managed
  main-thread `SetComponent` calls; the ~28 ms managed-record spike is gone.

## Risk / watch

Primary silent-failure risk is Burst rejecting a record helper. If the job fails
to Burst-compile, inline the field writes into the suffix loop (mirror
`WriteCommon` + `SpawnStateFor`) instead of reverting to the managed loop.

## Scope

Small — one system, ~1 field-pair + one loop moved, one loop deleted.

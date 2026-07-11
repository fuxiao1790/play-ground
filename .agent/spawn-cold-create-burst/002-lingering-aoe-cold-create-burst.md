# 002 — Lingering AOE cold-create into Burst job

**File:** [Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs)
(`LingeringAoeSpawnApplySystem` + `LingeringAoeSpawnJob`)

Identical shape to [001](001-impact-aoe-cold-create-burst.md), for the lingering
archetype and its record helper.

## Change

1. **ECB allocator** (line ~311): `Allocator.Temp` → `Allocator.TempJob`.

2. **Add to `LingeringAoeSpawnJob`:**
   ```csharp
   public EntityCommandBuffer Ecb;
   public EntityArchetype Archetype;
   ```

3. **Tail of `LingeringAoeSpawnJob.Execute()`** (after the chunk loop, before
   `ReuseCount.Value = commandIndex;`):
   ```csharp
   for (int i = commandIndex; i < Configs.Length; i++)
   {
       Entity entity = Ecb.CreateEntity(Archetype);
       AoeSpawnApplyUtility.RecordLingeringReset(Ecb, entity, Configs[i]);
   }

   ReuseCount.Value = commandIndex;
   ```
   `RecordLingeringReset` already records lifetime, pulse VFX, timed-spawn, and
   arming — matching the lingering archetype the chunk-write path fills.

4. **In `OnUpdate`**, pass `Ecb = createEcb`, `Archetype = _lingeringArchetype`
   into the job; delete the main-thread cold loop (lines ~346–351); recompute
   `coldCreateCount = commands.Length - reuseCount`; keep the
   `if (coldCreateCount > 0) createEcb.Playback(EntityManager);`.

## Acceptance criteria

- Compiles; `LingeringAoeSpawnJob` Burst-compiles clean.
- `LingeringAoeSpawnApplySystem.Reuse` + `.Cold` sum to the tick command count;
  cold-only start frame → `Reuse == 0`.
- `AoeSimulationTests` PlayMode suite passes (lingering-AOE lifetime, timed-spawn
  children still fire — confirms `TimedSpawnComponent` enable + state seeded on
  cold slots).
- Profiler: lingering spawn cost is Burst job self-time, not managed record.

## Risk / watch

Same as 001 — Burst-compile the record tree. Also confirm the timed-spawn enable
bit + `TimedSpawnStateComponent` seed applied on cold-created lingering AOEs
(`RecordLingeringReset` handles both), since multi-level interval chains depend
on it.

## Scope

Small — mirrors 001.

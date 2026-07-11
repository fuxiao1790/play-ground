# 003 — Projectile cold-create into Burst job

**File:** [Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs)
(`ProjectileSpawnApplySystem` + `ProjectileSpawnJob`)

Same shape as [001](001-impact-aoe-cold-create-burst.md), with two extra
deletions because the projectile cold path uses an **instance** helper and a
dedicated sub-marker.

## Change

1. **ECB allocator** (line ~111): `Allocator.Temp` → `Allocator.TempJob`.

2. **Add to `ProjectileSpawnJob`:**
   ```csharp
   public EntityCommandBuffer Ecb;
   public EntityArchetype Archetype;
   ```

3. **Tail of `ProjectileSpawnJob.Execute()`** (after the chunk loop, before
   `ReuseCount.Value = commandIndex;`):
   ```csharp
   for (int i = commandIndex; i < Commands.Length; i++)
   {
       Entity entity = Ecb.CreateEntity(Archetype);
       ProjectileSpawnApplySystem.RecordCommonProjectileReset(Ecb, entity, Commands[i].Faction, Commands[i]);
       ProjectileSpawnApplySystem.RecordTimedSpawnReset(Ecb, entity, Commands[i]);
   }

   ReuseCount.Value = commandIndex;
   ```
   `RecordCommonProjectileReset` and `RecordTimedSpawnReset` are already `static`
   — reachable from the nested job via the enclosing type name. They cover the
   contact-gate `AppendToBuffer` and timed-spawn seed, matching the chunk-write
   path.

4. **In `OnUpdate`**, pass `Ecb = createEcb`, `Archetype = _archetype` into the
   job. Delete the `using (ColdCreateMarker.Auto())` block and its loop
   (lines ~146–153). Recompute `coldCreateCount = commands.Length - reuseCount`;
   keep `if (coldCreateCount > 0) createEcb.Playback(EntityManager);`.

5. **Delete now-dead members:**
   - the instance method `CreateProjectileEntity` (lines ~175–180) — it only
     existed to close over `_archetype` on the main thread.
   - the `ColdCreateMarker` field (line ~34–35).
   Keep `RecordCommonProjectileReset` / `RecordTimedSpawnReset` (now called from
   the job).

## Acceptance criteria

- Compiles; `ProjectileSpawnJob` Burst-compiles clean; no remaining reference to
  `CreateProjectileEntity` or `ColdCreateMarker`.
- `ProjectileSpawnApplySystem.Reuse` + `.Cold` sum to the tick command count;
  cold-only start frame → `Reuse == 0`.
- `ProjectileCollisionSimulationTests` PlayMode suite passes (pierce, contact
  gate seed, tracking enable all still correct on cold-created projectiles).
- Profiler: projectile cold spawn is Burst job self-time; the main-thread
  `ProjectileSpawnApplySystem.ColdCreate` marker is gone (removed).

## Risk / watch

Same Burst-compile risk as 001. Extra watch: the `SeedContactGateTargetId`
`AppendToBuffer` path and the `ProjectileTrackingComponent` enable bit — both
must still apply on cold slots (they're in `RecordCommonProjectileReset`).

## Scope

Small-plus — one system, plus deleting one instance helper and one marker.

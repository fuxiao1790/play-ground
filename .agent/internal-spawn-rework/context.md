# Spawn Data Flow

## Entities involved

| Entity | Key buffers / components |
|---|---|
| Scope (AoeScope / ProjectileScope) | `AoeSpawnRequestElement`, `ProjectileSpawnRequestElement`, `CombatSpawnElement`, `CombatDamageElement`, `CombatTargetElement`, `VfxSpawnRequestElement` |
| AoE entity | `AoeTag`, `AoeActiveTag` (enableable), `AoeCollisionActiveTag`, `AoeIdentityComponent`, `AoeHitSpawnComponent`, `AoeContactGateElement`, `CombatRenderScope` (shared), `CombatRenderTypeId` (shared) |
| Projectile entity | `ProjectileTag`, `ProjectileActiveTag` (enableable), `ProjectileCollisionActiveTag`, `ProjectileIdentityComponent`, `ProjectileHitComponent`, `ProjectileContactGateElement`, optionally `ProjectileChildSpawnerTag` + `ProjectileChildSpawnerComponent` + `ProjectileChildSpawnStateComponent` |

---

## Path A — Initial spawn (game code → ECS entity)

```
Game code
  │  AoeRoot.Spawn(AoeSpawnCommand)
  │  ProjectileRoot.Spawn(ProjectileSpawnCommand, seedContactGateTargetId)
  │
  │  Root converts command → *SpawnRequestElement (bounds computed, render baked)
  │  EntityManager.GetBuffer<*SpawnRequestElement>(scopeEntity).Add(...)
  ▼
AoeSpawnSystem / ProjectileSpawnSystem  [SimulationSystemGroup]
  │  Dependency.Complete()
  │  reads *SpawnRequestElement buffers; groups by key:
  │    AoeSpawnKey         = (scopeIndex, typeId)
  │    ProjectileSpawnKey  = (scopeIndex, typeId, hasChildSpawner)
  │
  │  per bucket:
  │    deadSlots query filtered by (CombatRenderScope, CombatRenderTypeId)
  │    AssignSlices2A — main-thread scan of disabled entities → int2(cfgOffset, take) per chunk
  │
  │    claimed slots → *SpawnJob : IJobChunk (parallel)
  │                    writes all components directly into chunk arrays
  │                    sets ActiveTag / CollisionActiveTag / RenderActiveTag
  │
  │    unclaimed      → ECB.CreateEntity(archetype) cold-create
  │                     ECB.Playback() after all jobs
  ▼
Active AoE / Projectile ECS entities
```

**Notes:**
- Reuse path is zero-allocation: no ECB, chunk arrays written in-place.
- Cold-create path uses a single shared ECB per system update.
- `ProjectileSpawnSystem` maintains two dead-slot queries: `deadSlotsNoChildSpawner` / `deadSlotsWithChildSpawner` (different archetypes).
- Render data (`CombatRenderComponent`) is baked into the request element by the root at spawn time; spawn jobs copy it verbatim.

---

## Path B — On-hit spawn (collision → secondary AoE / projectile)

```
AoeCollisionSystem / ProjectileCollisionSystem  [SimulationSystemGroup]
  │  *CollisionJob : IJobEntity (parallel, Burst)
  │    spatial hash: NativeParallelMultiHashMap<long cellKey, int targetIndex>
  │    per active entity: collect candidate target indices → bounds + shape hit test
  │    if hit && HasSpawnEvent (ProjectileBurst / ImpactAoe / ImpactProjectile enabled):
  │      pendingSpawns.Write(CombatPendingSpawn { Scope, ids, Position, TargetPosition, Kind, ... })
  │    if hit && HasDamageEvent:
  │      pendingDamage.Write(CombatPendingDamage { ... })
  ▼
CombatHitFlushJob : IJob  (chained after collision job, one instance per collision system)
  │  NativeStream.Reader over pendingSpawns
  │  BufferLookup<CombatSpawnElement>[scope].Add(CombatSpawnElement { ... })
  │  BufferLookup<CombatDamageElement>[scope].Add(CombatDamageElement { ... })
  │  (NativeStreams disposed after flush job completes)
  ▼
CombatHitDispatchSystem  [PresentationSystemGroup]  ← different group, runs after Simulation
  │  CompleteDependency()
  │  per registered scope handler:
  │    reads CombatSpawnElement buffer
  │    ReplaySpawnsAndClear → spawnHandler(CombatHitContext, CombatSpawnElement)
  │    lambda registered by Root.BindWorld():
  │      AoeRoot    → HitSpawn?.Invoke(context, spawn)
  │      ProjectileRoot → HitSpawn?.Invoke(context, spawn)
  ▼
CombatSpawnRouter  (game code, event subscriber)
  │  OnPlayerProjectileHitSpawn → SpawnImpactAoe(playerAoeRoot)
  │                             → SpawnImpactProjectiles(playerProjectileRoot)
  │  OnMobProjectileHitSpawn   → SpawnImpactAoe(mobAoeRoot)
  │                             → SpawnImpactProjectiles(mobProjectileRoot)
  │  OnPlayerAoeHitSpawn       → SpawnProjectileBurst(playerProjectileRoot)
  │  OnMobAoeHitSpawn          → SpawnProjectileBurst(mobProjectileRoot)
  │
  │  Each handler calls Root.Spawn() → back to Path A (next frame)
  ▼
*SpawnRequestElement written to scope buffer → consumed next Simulation frame
```

**Spawn types by source:**

| Source | ImpactAoe | ImpactProjectile | ProjectileBurst |
|---|---|---|---|
| AoE hit | No | No | Yes (`AoeHitSpawnComponent.ProjectileBurst`) |
| Projectile hit | Yes | Yes | Yes (`ProjectileHitComponent.HitPayload`) |

**Frame boundary:** collision writes → flush → scope buffer → dispatch (Presentation) → Root.Spawn() → request buffer → spawn system picks up **next Simulation frame**.

---

## Path C — Timed child spawn (interval tick → ProjectileSpawnRequestElement)

```
ProjectileChildSpawnSystem  [SimulationSystemGroup, after ProjectileMovementSystem]
  │  ProjectileChildSpawnEntityJob : IJobEntity (parallel, Burst)
  │  filter: ProjectileTag + ProjectileActiveTag + ProjectileChildSpawnerTag
  │
  │  per entity:
  │    cooldown -= deltaTime
  │    while cooldown <= 0:  (catches multi-tick catch-up in one frame)
  │      tickIndex++
  │      for childIndex in [0, ChildCountPerTick):
  │        ComputeChildVelocity:
  │          Forward    → parent velocity direction
  │          SideSpray  → alternating left/right, fanned over SideSpreadDegrees
  │        Ecb.AppendToBuffer(chunkIndex, scope, ProjectileSpawnRequestElement)
  │          HasChildSpawner = 0  (children never recurse)
  │          childProjectileId = hash(parentId, spawnerId, tickIndex, childIndex)
  │          Render computed from ProjectileChildSpawnerComponent fields (baked at parent spawn)
  │      cooldown += IntervalSeconds + DeterministicJitter(parentId, spawnerId, tickIndex)
  │    write back cooldown + tickIndex
  ▼
EndSimulationEntityCommandBufferSystem  playback
  │  appends ProjectileSpawnRequestElement to scope's DynamicBuffer
  ▼
ProjectileSpawnSystem  [next frame]  → Path A
```

**Notes:**
- ECB `AppendToBuffer` (parallel writer) → requests land after `EndSimulationECB` plays back → **one frame delay** vs the tick that triggered them.
- Render data baked into `ProjectileChildSpawnerComponent` at parent spawn time by `ProjectileRoot.ChildSpawnerComponentFor()`; child spawn job never touches `ProjectileRoot`.
- `childProjectileId` is deterministic: stable across replay, avoids collision between siblings.
- Children have `HasChildSpawner = 0` — no recursive child spawners.

---

## Path D — Multi-shot fan-out (expand command → individual elements via NativeStream)

Added as part of the internal-spawn-rework. Sits between Path A's buffer write and the spawn system.

```
Root.Spawn(ProjectileSpawnCommand { Count > 1, SpreadDegrees, JitterDegrees })
  │  SpawnRequestFor() sets:
  │    Count = N, BaseDirection, Speed, SpreadDegrees, JitterDegrees
  │    JitterSeed = (uint)baseProjectileId * 2654435761u
  │    nextProjectileId incremented by N (reserves N IDs)
  │    Velocity = default (expand job computes per-shot)
  │  EntityManager.GetBuffer<ProjectileSpawnRequestElement>(scope).Add(command)
  ▼
ProjectileMultiExpandSystem  [SimulationSystemGroup, UpdateBefore(ProjectileSpawnSystem)]
  │  OnUpdate (main thread):
  │    collect all ProjectileSpawnRequestElement from all scope buffers → NativeArray
  │    clear scope buffers
  │    allocate NativeStream(totalCommands)
  │
  │  ProjectileMultiExpandJob : IJob  (Burst, worker thread)
  │    for each command ci:
  │      Stream.BeginForEachIndex(ci)
  │      if Count <= 1:
  │        Stream.Write(cmd)              ← pass-through, Velocity already set
  │      else:
  │        rng = Random(JitterSeed)
  │        for i in [0, Count):
  │          angle = -Spread*0.5 + Spread/(Count-1)*i   ← evenly spaced fan
  │          if JitterDegrees > 0: angle += rng.NextFloat(-jitter, jitter)
  │          elem.Count        = 1
  │          elem.ProjectileId = baseId + i
  │          elem.Velocity     = Rotate(BaseDirection, angle) * Speed
  │          elem.Render.RenderZ = baked from per-shot ProjectileId
  │          Stream.Write(elem)
  │      Stream.EndForEachIndex()
  │
  │  exposes: internal NativeStream PendingStream
  ▼
ProjectileSpawnSystem  (reads PendingStream instead of scope buffers)
  │  Dependency.Complete() completes expand job via chain
  │  NativeStream.Reader iterates ForEachCount slots
  │  per element (all Count == 1 at this point):
  │    group by ProjectileSpawnKey(scope.Index, typeId, hasChildSpawner)
  │  expandSys.PendingStream.Dispose() after reading
  │  → dead slot reuse / ECB cold-create (Path A, unchanged)
  ▼
Active Projectile ECS entities
```

**Key design points:**
- Fan-out math runs on worker thread (Burst IJob), not main thread.
- `Unity.Mathematics.Random` replaces `UnityEngine.Random` — Burst-safe, deterministic from seed.
- Callers (SkillSpawnTranslator, CombatSpawnRouter) write one command regardless of Count; no caller-side loops or angle math.
- `ProjectileSpawnRequestElement` is dual-use: Count > 1 = command consumed by expand job; Count == 1 = individual element consumed by spawn system. Comment in struct documents this.
- BoundsMin/BoundsMax computed once in Root (position + shape only, no velocity); copied verbatim to all N output elements.
- RenderZ patched per shot in expand job using the shot's unique ProjectileId.
- Count == 1 commands pass through expand job unchanged (no allocation overhead for single shots).

**Path C interaction (child spawns):**
Child spawns from `ProjectileChildSpawnSystem` use ECB (`EndSimulationECB`) → play back AFTER `ProjectileMultiExpandSystem` clears scope buffers. Child elements have Count == 1 and pre-computed Velocity, so they're picked up by `ProjectileMultiExpandSystem` next frame and pass through the expand job unchanged. One-frame delay unchanged vs. prior behavior.

---

## Shared infrastructure

**Pool / reuse:** both `AoeSpawnSystem` and `ProjectileSpawnSystem` pool `List<*SpawnRequestElement>` via `_listPool` (clear + return each frame). No managed allocation on hot path once warmed up.

**Scope entity created by:** `AoeRoot.BindWorld()` / `ProjectileRoot.BindWorld()` — adds all required buffers and registers the `HitSpawn` lambda with `CombatHitDispatchSystem`.

**`CombatHitFlushJob`** is instantiated independently by each collision system with its own stream pair. Both can run concurrently; they write to different scope entities so `BufferLookup` writes don't conflict.

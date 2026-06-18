# Context 003 — Final Data Flow (step by step)

Read with `002-target-architecture.md`. This traces every spawn/damage path from producer to consequence in the **target** architecture, marking where logic is preserved vs intentionally changed. (Naming follows D-NAMING-EVENTCMD: `...SpawnEvent` = intent/multiplicity, `...SpawnCommand` = one resolved entity.)

---

## 1. Projectile spawn — external (managed attack fire)

```
CombatRoot.Spawn(ProjectileSpawnRequest)                   [main thread, Update]
  → convert request (Vector2 / managed snapshots) → blittable ProjectileSpawnEvent (Count may be > 1)
  → append to scope DynamicBuffer<ProjectileSpawnEvent>     [low-volume managed submission]

ProjectileSpawnExpansionSystem.OnUpdate                     [SimulationSystemGroup]
  → complete producer deps
  → drain (scope buffer + EventQueue) → NativeArray<ProjectileSpawnEvent>; Clear both
  → expansion job: fan out Count shots (spread + jitter + per-shot velocity + render-Z
    + per-shot id = BaseProjectileId + i) → route each to the per-shape command container
    (BasicProjectile vs ChildSpawnerProjectile, by HasChildSpawner) as ProjectileSpawnCommand

<shape>ProjectileSpawnApplySystem.OnUpdate                  [one system per real shape]
  → drain its command container → NativeArray<ProjectileSpawnCommand>
  → reuse WithDisabled<Active> slots of its fixed archetype (IJobChunk) / ECB cold-create overflow
  → enable Active, ProjectileCollisionActiveTag (= NeedsCollision), CombatRenderActiveTag
```
**Preserved (logic):** id assignment, fan-out math (spread/jitter/per-shot velocity/render-Z), reuse/cold-create machinery, `NeedsCollision` opt-in. **Changed (architecture):** transport is `ProjectileSpawnEvent` (intent); the one-entity type is `ProjectileSpawnCommand`; multiplicity is resolved only in expansion; occupancy flag is the generic `Active`; apply is split per shape with **no shape-key branch** (D-SHAPE-EXPLICIT).

## 2. Projectile spawn — timed child

```
TimedProjectileSpawnSystem (IJobEntity over child-spawner archetype)   [producer]
  → timed catch-up loop (unchanged math) per due tick/child
  → ProjectileSpawnEvent (Count=1, HasChildSpawner=0) → EventQueue.AsParallelWriter()
  → expansion → BasicProjectile container → BasicProjectileSpawnApplySystem
```
**Preserved:** catch-up loop, side-spray/forward pattern math, deterministic child id + interval jitter. **Changed:** output is an enqueued event; the `EndSimulationEntityCommandBufferSystem` dependency is gone.

## 3. Projectile spawn — impact consequence (from collision)

```
ProjectileCollisionSystem qualified hit, ImpactProjectile snapshot present
  → ProjectileSpawnPipeline.BuildImpactProjectileEvent(snapshot, hitPos, targetPos)
       (id-hash + fire-back-toward-source direction + Count/spread carried as intent)
  → EventQueue.AsParallelWriter()
AoeCollisionSystem qualified hit, ProjectileBurst snapshot present
  → ProjectileSpawnPipeline.BuildBurstEvent(...) (fire-toward-target direction)
  → EventQueue.AsParallelWriter()
```
**Preserved:** the exact request-building logic from `CombatSpawnConvertJob` (`HashId` salts, `DirectionFromTo` invert rules, tracking/render builders, `seedContactGateTargetId`) — relocated verbatim into the shared helper, now producing a `ProjectileSpawnEvent`. **Changed:** no generic `CombatPendingSpawn`, no `CombatSpawnConvertJob`; collision writes the final typed event directly (§7.2). Bursts fan out via the same expansion as all events.

## 4. AoE spawn — external / impact / status

```
CombatRoot.Spawn(AoeSpawnRequest) → build AoeSpawnEvent → scope DynamicBuffer<AoeSpawnEvent>
ProjectileCollisionSystem, ImpactAoe snapshot present → AoeSpawnEvent → AoeEventQueue
(status-effect AoE follow-ups keep their current path into the same event)

AoeSpawnExpansionSystem
  → drain queue + scope buffer → NativeArray<AoeSpawnEvent>; Clear
  → resolve world bounds (ComputeWorldBounds) → AoeSpawnCommand (1:1)
  → also append spawn VFX request (Trigger=0) to scope VfxSpawnRequestElement

AoeSpawnApplySystem → single AoE shape → reuse WithDisabled<Active> / cold-create
```
**Preserved:** `AoeRequestFor` field mapping, `AppendImpactAoe` geometry/render/id-hash logic (relocated to a shared helper emitting `AoeSpawnEvent`), pulse-vs-lingering decision (`Lifetime <= 0 ⇒ pulse`), spawn-time VFX. **Changed:** transport is `AoeSpawnEvent`/`AoeSpawnCommand`; impact-AoE build moves out of `CombatSpawnConvertJob`; occupancy is `Active`.

## 5. Damage replay (rebuilt — Entity-keyed, native transport)

```
ProjectileCollisionSystem / AoeCollisionSystem qualified hit, ContactDamage present
  → DamageReplayEvent { TargetProxy = hit proxy entity; Damage; HitPosition; HitDirection; … }
  → NativeQueue<DamageReplayEvent>.ParallelWriter

Damage finalize (after both collisions)
  → NativeQueue → NativeArray<DamageReplayEvent> (frozen)

DamageDispatchBridge                                          [PresentationSystemGroup]
  → group entries by TargetProxy (Entity)
  → roll crit on main thread (UnityEngine.Random)
  → resolve Entity → TargetCompanion (managed) → ICombatTarget   [the ONLY companion reader, §8.4]
  → ICombatTarget.ReceiveHits(hits)
  → Clear() the damage queue for the frame
```
**Preserved (logic):** atomic per-hit, group-by-target, crit roll on the main thread, one `ReceiveHits` per target group. **Changed (architecture):** identity is `Entity TargetProxy` (not `TargetId`+`Faction`); transport is `NativeQueue → NativeArray` (not `NativeStream → CombatHitFlushJob → CombatDamageElement` buffer); `CombatHitFlushJob` and `CombatDamageElement` are deleted; dispatch resolves the target via the managed companion on the proxy.

### 5a. Target proxy lifecycle (feeds §5 and collision)
```
ICombatTarget GameObject.OnEnable  → create proxy Entity + attach TargetCompanion + TargetFaction
ICombatTarget GameObject.Update     → push TargetPosition / TargetCollisionShape into the proxy   [before simulation]
ICombatTarget leaves simulation     → delete proxy Entity immediately (Update/LateUpdate)
Collision spatial query             → over proxy entities (TargetPosition + shape + faction)
```
**Replaces:** `CombatTargetSync.SyncToBuffer` filling `CombatTargetElement`, and the `targetsById` dictionary. **Preserved:** `CombatCollisionMath.ComputeWorldBounds`, the spatial-hash collision math, `IsCombatTargetActive` gating (now via proxy presence + an alive flag).

## 6. Lifetime expiry

```
CombatLifetimeSystem (over Active + enabled CombatLifetimeComponent)
  → Remaining -= dt; if <= 0: disable Active + CombatRenderActiveTag; despawn VFX (Trigger=2)

AoePulseVfxSystem (AoE-only, lingering pulse) → periodic pulse VFX (Trigger=3)
```
Pulse AOEs have `CombatLifetimeComponent` **disabled** at spawn, so the unified system skips them; `AoeCollisionSystem` deactivates them the same tick. **Preserved:** projectile despawn VFX area = `max(render.VisualScale.xy)`, pulse cadence. **Intentionally changed (minor):** AoE despawn VFX area uses render scale instead of `AoeAreaComponent.Size` (D-LIFETIME-VFX).

---

## Aggregation / filtering summary
- **Fan-out (one→N)** happens only in `ProjectileSpawnExpansionSystem`.
- **Shape routing** happens in expansion; apply never re-derives shape (D-SHAPE-EXPLICIT).
- **Consequence filtering** happens inline in collision by snapshot presence — as today.
- **Damage grouping (N hits→per-target)** happens only in `DamageDispatchBridge`, now keyed by proxy `Entity`.
- No per-target-per-tick condensation (§8.1 — preserved semantics).

# Context 003 — Final Data Flow (step by step)

Read with `002-target-architecture.md`. This traces every spawn/damage path from producer to consequence in the **target** architecture, marking where behavior is preserved vs intentionally changed.

---

## 1. Projectile spawn — external (managed attack fire)

```
CombatRoot.Spawn(ProjectileSpawnCommand managedCmd)         [main thread, Update]
  → build ProjectileSpawnEvent (intent; Count may be > 1)
  → append to scope DynamicBuffer<ProjectileSpawnEvent>     [low-volume managed submission]

ProjectileSpawnExpansionSystem.OnUpdate                     [SimulationSystemGroup]
  → complete producer deps
  → drain (scope buffer + EventQueue) → NativeArray<ProjectileSpawnEvent>; Clear both
  → expansion job: for each event, fan out Count shots (spread + jitter + per-shot velocity
    + per-shot render-Z + per-shot id = BaseProjectileId + i) → ProjectileSpawnCommandData out

ProjectileSpawnApplySystem.OnUpdate
  → drain command output → NativeArray<ProjectileSpawnCommandData>
  → bucket by (faction, typeId, hasChildSpawner)
  → reuse WithDisabled<ProjectileActiveTag> slots (IJobChunk) / ECB cold-create overflow
  → enable ProjectileActiveTag, ProjectileCollisionActiveTag (= NeedsCollision), CombatRenderActiveTag
```
**Preserved:** id assignment (`CombatRoot.nextProjectileId += command.Count`, base = +1), fan-out math (spread angle, jitter, per-shot velocity, render-Z slot stepping), reuse/cold-create, `NeedsCollision` opt-in. **Changed:** the transport is `ProjectileSpawnEvent` (intent) not the dual-use element; multiplicity is resolved in expansion, never in apply.

## 2. Projectile spawn — timed child

```
TimedProjectileSpawnSystem (IJobEntity over child-spawner archetype)   [producer]
  → timed catch-up loop (unchanged math) per due tick/child
  → ProjectileSpawnEvent (Count=1, HasChildSpawner=0) → EventQueue.AsParallelWriter()
  → expansion → apply (as §1; Count==1 passes through expansion unchanged)
```
**Preserved:** catch-up loop, side-spray/forward pattern math, deterministic child id + interval jitter (`ChildProjectileId`, `DeterministicJitter`, `NextIntervalSeconds`). **Changed:** output is an enqueued event, not an `ECB.AppendToBuffer` onto the scope buffer; the `EndSimulationEntityCommandBufferSystem` dependency is removed.

## 3. Projectile spawn — impact consequence (from collision)

```
ProjectileCollisionSystem qualified hit, ImpactProjectile snapshot present
  → ProjectileSpawnPipeline.BuildImpactProjectileEvent(hit snapshot, hit pos, target pos)
       (id-hash + fire-back-toward-source direction + Count/spread carried as intent)
  → EventQueue.AsParallelWriter()
AoeCollisionSystem qualified hit, ProjectileBurst snapshot present
  → ProjectileSpawnPipeline.BuildBurstEvent(...) (fire-toward-target direction)
  → EventQueue.AsParallelWriter()
```
**Preserved:** the exact request-building logic currently in `CombatSpawnConvertJob` (`HashId` salts, `DirectionFromTo` invert/no-invert, `BuildProjectileRequest`, `TrackingFor`, `ProjectileRender`, `seedContactGateTargetId = pending.TargetId` for impact projectiles) — relocated verbatim into the shared helper, now producing a `ProjectileSpawnEvent` instead of a `ProjectileSpawnRequestElement`. **Changed:** no generic `CombatPendingSpawn` stream, no `CombatSpawnConvertJob` pass; collision writes the final typed event directly (design §7.2). `Count>1` bursts are fanned out by the same expansion as all other events (previously `CombatSpawnConvertJob` set `BaseDirection/Speed/Spread/JitterSeed` for the dual-use element which `ProjectileMultiExpandSystem` then expanded — same net result, one fewer stage type).

## 4. AoE spawn — external / impact / status

```
CombatRoot.Spawn(AoeSpawnCommand) → scope DynamicBuffer<AoeSpawnEvent>
ProjectileCollisionSystem, ImpactAoe snapshot present → AoeSpawnEvent → AoeEventQueue
(status-effect AoE follow-ups keep their current path into the same event)

AoeSpawnExpansionSystem
  → drain queue + scope buffer → NativeArray<AoeSpawnEvent>; Clear
  → resolve world bounds (ComputeWorldBounds) → AoeSpawnCommandData (1:1)
  → also append spawn VFX request (Trigger=0) to scope VfxSpawnRequestElement (as AoeSpawnSystem does today)

AoeSpawnApplySystem → bucket (faction, typeId) → reuse/cold-create
```
**Preserved:** `AoeRequestFor` field mapping, `AppendImpactAoe` geometry/render/id-hash logic (relocated to a shared helper, emitting `AoeSpawnEvent`), pulse-vs-lingering decision (`Lifetime <= 0 ⇒ pulse`), spawn-time VFX. **Changed:** transport is `AoeSpawnEvent`; the impact-AoE build moves out of `CombatSpawnConvertJob` into the collision/helper.

## 5. Damage replay (preserved; renamed)

```
ProjectileCollisionSystem / AoeCollisionSystem qualified hit, ContactDamage present
  → DamageReplayEvent (was CombatPendingDamage) → pendingDamage NativeStream
  → CombatHitFlushJob → scope CombatDamageElement buffer (unchanged)

DamageDispatchBridge (was CombatHitDispatchSystem)             [PresentationSystemGroup]
  → group consecutive entries by (TargetId, Faction)
  → roll crit on main thread (UnityEngine.Random)
  → resolve ICombatTarget via CombatRoot.TryGetByFaction(...).TargetsById
  → ICombatTarget.ReceiveHits(hits); Clear() buffer
```
**Preserved entirely** (atomic per-hit, group-by-target, crit on main thread, one `ReceiveHits` per target group). **Changed:** struct/system names only; the `CombatDamageElement` clear is owned by exactly one `OrderFirst` system instead of being cleared by both `ProjectileSimulationSystem` and `AoeSimulationSystem`.

## 6. Lifetime expiry

```
CombatLifetimeSystem (over active + enabled CombatLifetimeComponent)
  → Remaining -= dt; if <= 0: disable active tag + CombatRenderActiveTag; despawn VFX (Trigger=2)

AoePulseVfxSystem (AoE-only, lingering pulse) → periodic pulse VFX (Trigger=3)
```
Pulse AOEs have `CombatLifetimeComponent` **disabled** at spawn, so the unified system skips them; `AoeCollisionSystem` deactivates them the same tick (unchanged). **Preserved:** projectile despawn VFX area = `max(render.VisualScale.xy)`, pulse VFX cadence. **Intentionally changed (minor, low-risk):** AoE *despawn* VFX area now uses render scale instead of `AoeAreaComponent.Size` (the unified system is domain-neutral). Recorded in decision log D-LIFETIME-VFX.

---

## Aggregation / filtering summary
- **Fan-out (one→N)** happens only in `ProjectileSpawnExpansionSystem`.
- **Consequence filtering** ("does this hit produce damage / spawn / vfx") happens inline in the collision job, by snapshot presence — exactly as today.
- **Damage grouping (N hits→per-target)** happens only in `DamageDispatchBridge` at presentation — exactly as today.
- No new aggregation is introduced; no per-target-per-tick damage condensation (design §8.1 — preserved semantics).

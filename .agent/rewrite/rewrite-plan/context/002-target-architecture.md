# Context 002 — Target Architecture (decisive)

This is the architecture after the rewrite. It is **chosen, not optional**. Where the design doc offered choices, the choice is made here and recorded in `005-decision-log.md`. Do not reopen these.

**Governing priority:** faithful realization of the direction's architecture. Old *logic* is preserved where possible; old *architecture* is not. Churn (renames, archetype changes, target-bridge rebuild) is accepted — see 005's governing principle.

Scope of this rewrite (the design's §11 "Rewritten / replaced" rows, fully realized):
1. Split spawn intent from allocation intent with correct Event/Command names (R3, D-NAMING-EVENTCMD).
2. Make a single explicit expansion phase own all spawn math; one apply system per real shape (R4, §5.3, §5.4).
3. Replace the dual-use buffer transport + convert job with typed events and native-container phase discipline (§6, §7.2).
4. Unify lifetime over a generic `Active` flag (§4, §3).
5. Adopt the generic `Active` occupancy flag (§3).
6. Adopt per-target ECS proxy entities + managed companion, and the native-container damage transport (§8).

**Still deferred / out of scope** (with design support): damage condensation (§12), timed-AoE child spawns and AoE scatter (kept as structural slots only — D-AOE-EXPANSION-MINIMAL), VFX/audio consequence streams (§12), ECS-owned target health / full hybrid model (§12). Movement, tracking, collision math, rendering/VFX dispatch, and authoring/ScriptableObject *definitions* are preserved.

---

## 1. Final type shapes

### 1.1 Projectile spawn intent — `ProjectileSpawnEvent`
Plain unmanaged struct, `: IBufferElementData` (both a `NativeQueue<T>` element for internal producers and a scope `DynamicBuffer<T>` element for low-volume managed submission). Lives in `System/Projectile/ProjectileSpawnPipeline.cs`. This is **the** intent type — authoring builds it, `CombatRoot` submits it, producers enqueue it.

Fields (intent + multiplicity + resolved template):
```
CombatFaction Faction;
int   TypeId;
int   BaseProjectileId;          // expansion assigns BaseProjectileId + i
int   HasChildSpawner;           // shape selector → which command container expansion routes to
int   SeedContactGateTargetId;
float2 Position;
float2 BaseDirection;            // unit direction; expansion derives per-shot velocity
float  Speed;
// multiplicity — consumed and DISCARDED by expansion, never reaches a command:
int    Count;
float  SpreadDegrees;
float  JitterDegrees;
uint   JitterSeed;
// resolved per-entity template (everything apply needs except per-shot velocity/id/render-Z):
int    PierceRemaining;
float  RepeatHitCooldownSeconds;
float  Lifetime, Radius, RotationRadians;
float2 HalfExtents;
CombatShapeType ShapeType;
ProjectileHitPayload HitPayload;                 // includes ImpactAoe + ImpactProjectile depth-1 snapshots
ProjectileTrackingComponent Tracking;
CombatRenderComponent Render;
ProjectileChildSpawnerComponent ChildSpawner;       // valid when HasChildSpawner != 0
ProjectileChildSpawnStateComponent ChildSpawnState; // valid when HasChildSpawner != 0
```

### 1.2 Projectile allocation intent — `ProjectileSpawnCommand` (ECS)
Plain unmanaged struct, native-container only (NOT a buffer element). **Named `ProjectileSpawnCommand` — no `Data` suffix** (D-NAMING-EVENTCMD; the managed authoring `ProjectileSpawnCommand` is renamed `ProjectileSpawnRequest`, freeing the name for the type that genuinely means one entity). Lives in `System/Projectile/ProjectileSpawnPipeline.cs`.

Exactly one concrete entity. **No `Count`, `SpreadDegrees`, `JitterDegrees`, `JitterSeed`, `BaseDirection`, `Speed`.** Velocity, bounds, render-Z, final id already resolved:
```
CombatFaction Faction;
int   ProjectileId, TypeId, HasChildSpawner;   // HasChildSpawner now only records which apply path emitted it
int   PierceRemaining;
float RepeatHitCooldownSeconds;
int   SeedContactGateTargetId;
float Lifetime, Radius, RotationRadians;
float2 Position, Velocity, HalfExtents, BoundsMin, BoundsMax;
CombatShapeType ShapeType;
ProjectileHitPayload HitPayload;
ProjectileTrackingComponent Tracking;
CombatRenderComponent Render;     // RenderZ already resolved per-id
ProjectileChildSpawnerComponent ChildSpawner;
ProjectileChildSpawnStateComponent ChildSpawnState;
```
There is **one `ProjectileSpawnCommand` struct shared by the shape containers**; the *containers/apply systems* are per-shape (D-SHAPE-EXPLICIT), not the struct.

> Invariant (§5.4): if any apply system reads `Count`/`Spread`/`Jitter`, the boundary leaked. Those fields do not exist on `ProjectileSpawnCommand`.

### 1.3 AoE — `AoeSpawnEvent` / `AoeSpawnCommand`
Mirror the projectile split (§5.7). Live in `System/Aoe/AoeSpawnPipeline.cs`. AoE has no multiplicity today, so `AoeSpawnEvent` carries no fan-out fields; AoE expansion is a 1:1 transform resolving world bounds (D-AOE-EXPANSION-MINIMAL). `AoeSpawnEvent : IBufferElementData`; `AoeSpawnCommand` is native-only. The managed `AoeSpawnCommand` authoring DTO is renamed `AoeSpawnRequest`; `CombatRoot.Spawn(AoeSpawnRequest)` builds the blittable `AoeSpawnEvent`.

### 1.4 Generic occupancy — `Active`
```csharp
public struct Active : IComponentData, IEnableableComponent {}
```
Shared occupancy flag for all reusable runtime entities (projectile, AoE, future) — design §3, D-ACTIVE-GENERIC. Replaces `ProjectileActiveTag` and `AoeActiveTag`. Slot kind = marker component presence (`ProjectileTag`/`AoeTag` + shape markers); slot alive = `Active` enabled. Reuse query: `WithDisabled<Active>()` + domain/shape markers. The per-feature opt-in tags (`CombatRenderActiveTag`, collision-active) are a different concept and stay.

### 1.5 Damage replay — `DamageReplayEvent` (Entity-keyed, native transport)
Design §8.1 + D-PROXY-ENTITY + D-DAMAGE-TRANSPORT.
```csharp
public struct DamageReplayEvent   // blittable
{
    public Entity        TargetProxy;   // identity — replaces TargetId + Faction
    public DamageSnapshot Damage;       // pre-roll amount/flags carried for the main-thread crit roll
    public float2        HitPosition;
    public float2        HitDirection;
    public CombatHitKind Kind;
    public EntityId      SourceNodeId;
    public CombatStatusEffectSnapshot StackEffect;
    public bool          DirectDamageEnabled;
    // crit inputs kept for the main-thread roll (CritChance/CritMultiplier/SourceId/TypeId)
}
```
Transport: collision writes into a `NativeQueue<DamageReplayEvent>.ParallelWriter`; after both collisions a finalize step freezes it to a `NativeArray<DamageReplayEvent>`; `DamageDispatchBridge` consumes the array, then the owner `Clear()`s the queue (§6). **`CombatHitFlushJob` and the scope `CombatDamageElement` buffer are removed.**

### 1.6 Target proxy entity + companion
Each `ICombatTarget` GameObject owns a proxy `Entity` (D-PROXY-ENTITY, §8.2/8.3). Components:
```
TargetProxyTag            // marker
TargetPosition  { float2 }        // pushed by the GameObject each Update()
TargetCollisionShape { CombatShapeType, Radius, HalfExtents, RotationRadians, BoundsMin/Max, Mask }
TargetFaction   { CombatFaction }
TargetCompanion : IComponentData  // managed reference to the GameObject (class component); read ONLY by DamageDispatchBridge (§8.4)
```
Collision's spatial query reads proxy entities (a query/spatial-hash over `TargetPosition` + `TargetCollisionShape` + `TargetFaction`) instead of the synced `CombatTargetElement` buffer.

### 1.7 Unified lifetime — `CombatLifetimeComponent`
```csharp
// enableable; ENABLED on spawn for finite lifetimes, DISABLED for pulse AOEs (deactivated by
// collision the same tick). CombatLifetimeSystem counts it down and disables Active on expiry.
public struct CombatLifetimeComponent : IComponentData, IEnableableComponent { public float Remaining; }
```
Replaces `ProjectileLifetimeComponent` and `AoeLifetimeComponent.RemainingLifetime`. AoE `IsPulse` becomes "`CombatLifetimeComponent` disabled" (R2). Operates over the generic `Active` flag.

---

## 2. Final system responsibilities

### Projectile spawn pipeline (replaces ChildSpawn + MultiExpand + SpawnSystem)
1. **`TimedProjectileSpawnSystem`** (`ISystem`, producer) — `IJobEntity` over the child-spawner archetype; timed catch-up loop enqueues `ProjectileSpawnEvent` into `ProjectileSpawnExpansionSystem`'s `NativeQueue<ProjectileSpawnEvent>.ParallelWriter`. No ECB, no buffer append.
2. **`ProjectileSpawnExpansionSystem`** (`SystemBase`) — owns `NativeQueue<ProjectileSpawnEvent> EventQueue` (Persistent). OnUpdate: complete producers; drain `EventQueue` + the scope's managed-submission `ProjectileSpawnEvent` buffer into one frozen `NativeArray`; clear both; run the expansion job that resolves count/spread/jitter/direction/velocity/bounds/render-Z/id and **routes each resolved entity into the per-shape command container** for its shape.
3. **Per-shape apply systems** (`SystemBase`, D-SHAPE-EXPLICIT) — one per real shape:
   - `BasicProjectileSpawnApplySystem` (no child-spawner archetype),
   - `ChildSpawnerProjectileSpawnApplySystem` (child-spawner archetype).
   Each drains its own command container → array; reuses `WithDisabled<Active>` slots of its fixed archetype via `IJobChunk`; cold-creates overflow via ECB with that archetype's exact component set. No shape-key branch inside apply. The proven reuse/cold-create machinery is kept; only the input type and the active-flag query (`Active`) change.

### AoE spawn pipeline (replaces AoeSpawnSystem)
1. **`AoeSpawnExpansionSystem`** (`SystemBase`) — owns `NativeQueue<AoeSpawnEvent>`; drains queue + scope buffer; resolves world bounds; emits `AoeSpawnCommand` (1:1) into the single AoE command container; also emits the spawn-time VFX request (`Trigger=0`).
2. **`AoeSpawnApplySystem`** — the one AoE shape; reuse `WithDisabled<Active>` + cold-create. Body kept from `AoeSpawnSystem`, input changed to `AoeSpawnCommand`, active flag changed to `Active`.

> No `TimedAoeSpawnSystem` yet (no behavior requires it; §5.7 lists it as future). The pipeline supports adding one without restructuring.

### Collision (final producer of typed consequence events — §7.2)
`ProjectileCollisionSystem` / `AoeCollisionSystem` keep detection + inline gate + source self-mutation, now over **proxy entities** and the generic `Active` flag. On a qualified hit they write final typed events directly:
- `ContactDamage` present → `DamageReplayEvent` (with `TargetProxy = hit proxy entity`) into the damage queue.
- `ImpactProjectileSpawn` present → `ProjectileSpawnPipeline.BuildImpactProjectileEvent(...)` → `ProjectileSpawnEvent` → EventQueue.
- `ImpactAoeSpawn` / `AoeProjectileBurst` present → `AoeSpawnEvent` / `ProjectileSpawnEvent` → queue.
- VFX → unchanged (`VfxPendingSpawn` → `VfxStreamFlushJob`).

`CombatSpawnConvertJob` and `CombatPendingSpawn` are deleted (D-CONVERT-RELOCATE).

### Target bridge (rebuilt — D-PROXY-ENTITY)
- The `ICombatTarget` MonoBehaviour creates its proxy entity on enable, pushes `TargetPosition`/`TargetCollisionShape` each `Update()` (before simulation), and deletes the proxy immediately when leaving simulation (`LateUpdate`/disable). The managed companion is attached at creation.
- `CombatTargetRegistry` remains the managed registry of live targets; `CombatTargetSync`/`CombatTargetSyncSystem` (buffer fill) are replaced by proxy creation/position-push. `ICombatTarget` gains the proxy-lifecycle surface (CreateProxy/PushState/DeleteProxy or equivalent).

### Damage dispatch (D-DAMAGE-TRANSPORT, §8.4)
`DamageDispatchBridge` (`PresentationSystemGroup`) reads the finalized `NativeArray<DamageReplayEvent>`, groups by `TargetProxy`, rolls crit on the main thread, resolves `Entity → TargetCompanion → ICombatTarget`, calls `ReceiveHits`. It is the **only** approved reader of the companion (§8.4). It (or the queue owner) clears the damage queue for the frame.

### Lifetime (§4)
`CombatLifetimeSystem` (`ISystem`, `System/Common/`) — over `(Active, enabled CombatLifetimeComponent)`, counts down, disables `Active` + `CombatRenderActiveTag`, emits despawn VFX (`Trigger=2`, area = render scale, both domains). `AoePulseVfxSystem` (AoE-only) keeps pulse VFX (`Trigger=3`). `ProjectileLifetimeSystem` / `AoeLifetimeSystem` deleted.

---

## 3. Final native-container ownership & phase rules (§6)

| Container | Owner | Writers | Reader / finalize |
|---|---|---|---|
| `NativeQueue<ProjectileSpawnEvent>` | `ProjectileSpawnExpansionSystem` | `TimedProjectileSpawnSystem`, both collisions (impact/burst) via `.AsParallelWriter()` | expansion: complete → drain to `NativeArray` → `Clear()` |
| scope `DynamicBuffer<ProjectileSpawnEvent>` | scope entity (managed submission only) | `CombatRoot.Spawn` (main thread) | expansion: drain + `Clear()` |
| per-shape command container `NativeQueue<ProjectileSpawnCommand>` ×N shapes | `ProjectileSpawnExpansionSystem` | expansion job (ParallelWriter) | each shape's apply: complete → `NativeArray` by ranges → `Clear()` |
| `NativeQueue<AoeSpawnEvent>` + scope buffer | `AoeSpawnExpansionSystem` | collisions, `CombatRoot.Spawn` | expansion finalize |
| `NativeQueue<DamageReplayEvent>` | `DamageDispatchBridge` (owns the handle; finalizes) | both collisions (ParallelWriter) | finalize → `NativeArray` → bridge consumes → `Clear()` |

Phase contract (§6): no system reads a queue still being written; no system mutates command/damage data after it is finalized into an array; producers run before the matching expansion/finalize (§9 order); containers are created/cleared/disposed by their owner only; cross-system access uses `World.GetExistingSystemManaged<T>()`.

---

## 4. Rejected alternatives (summary; full text in 005)
- **Keep managed `...SpawnCommand` names + ECS `...CommandData` suffix** — rejected (D-NAMING-EVENTCMD): the multiplicity-bearing authoring type is an Event by R3; correct names matter more than rename churn.
- **One bucketed apply system keyed on a runtime shape field** — rejected (D-SHAPE-EXPLICIT): re-derives shape in apply, violating §5.3/§5.4; use one apply system per real shape.
- **Keep `TargetId`+`Faction` damage identity + `CombatTargetElement` sync** — rejected (D-PROXY-ENTITY): §8 mandates proxy entities + companion; the direction wants GameObjects represented as entities.
- **Keep `CombatHitFlushJob` + `CombatDamageElement` buffer** — rejected (D-DAMAGE-TRANSPORT): §8.1 prescribes `NativeQueue → NativeArray`; the flush stage/buffer are removed.
- **Keep per-domain active tags** — rejected (D-ACTIVE-GENERIC): §3 specifies one generic `Active`.

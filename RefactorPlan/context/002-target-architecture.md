# Context 002 — Target Architecture (decisive)

This is the architecture after the rewrite. It is **chosen, not optional**. Where the design doc offered choices, the choice is made here and recorded in `005-decision-log.md`. Do not reopen these.

Scope of this rewrite (the design's §11 "Rewritten / replaced" rows, grounded):
1. Split spawn intent from allocation intent (R3).
2. Make a single explicit expansion phase own all spawn math (R4, §5.4).
3. Replace the dual-use buffer transport + convert job with typed events and native-container phase discipline (§6, §7.2).
4. Unify lifetime (§4).
5. Reshape the damage replay path to the contract vocabulary and a single bridge (§8).

**Explicitly deferred / out of scope** (see decision log): generic single `Active` flag (D2), per-target ECS proxy entities and `DamageReplayEvent.TargetProxy : Entity` (D1), damage condensation (design §12), timed AoE child spawns and AoE scatter (kept as structural slots only), VFX/audio consequence streams, free-list reuse. VFX, movement, tracking, collision math, authoring, and the target registry/sync structure are preserved.

---

## 1. Final type shapes

### 1.1 Projectile spawn intent — `ProjectileSpawnEvent`
New plain unmanaged struct, **also** `: IBufferElementData` (so it can be both a `NativeQueue<T>` element for internal producers and a scope `DynamicBuffer<T>` element for the low-volume managed submission path). Lives in a new `System/Projectile/ProjectileSpawnPipeline.cs`.

Fields (intent + multiplicity + resolved template):
```
CombatFaction Faction;
int   TypeId;
int   BaseProjectileId;          // id base; expansion assigns BaseProjectileId + i
int   HasChildSpawner;           // shape selector carried through to apply
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
float  Lifetime;
float  Radius;
float  RotationRadians;
float2 HalfExtents;
CombatShapeType ShapeType;
ProjectileHitPayload HitPayload;            // includes ImpactAoe + ImpactProjectile depth-1 snapshots
ProjectileTrackingComponent Tracking;
CombatRenderComponent Render;
ProjectileChildSpawnerComponent ChildSpawner;       // valid when HasChildSpawner != 0
ProjectileChildSpawnStateComponent ChildSpawnState; // valid when HasChildSpawner != 0
```

### 1.2 Projectile allocation intent — `ProjectileSpawnCommandData`
New plain unmanaged struct (NOT a buffer element — carried only in native containers). **Named with the `Data` suffix to avoid colliding with the existing managed authoring type `ProjectileSpawnCommand`** (D-NAMING). Lives in `System/Projectile/ProjectileSpawnPipeline.cs`.

Exactly one concrete entity. **No `Count`, `SpreadDegrees`, `JitterDegrees`, `JitterSeed`, `BaseDirection`, `Speed`.** Velocity, bounds, render-Z, and final id are already resolved:
```
CombatFaction Faction;
int   ProjectileId;
int   TypeId;
int   HasChildSpawner;           // shape key for apply bucketing
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

> Invariant (design §5.4): if any apply system ever reads `Count`/`SpreadDegrees`/`JitterDegrees`/`JitterSeed`, the phase boundary has leaked. These fields do not exist on `ProjectileSpawnCommandData`.

### 1.3 AoE — `AoeSpawnEvent` / `AoeSpawnCommandData`
Mirror the projectile split (design §5.7 — AoE uses the identical pipeline). Live in `System/Aoe/AoeSpawnPipeline.cs`. AoE has **no multiplicity today**, so `AoeSpawnEvent` carries no fan-out fields; AoE expansion is a 1:1 transform that resolves world bounds. The stage exists for structural parity and future scatter (kept minimal on purpose). `AoeSpawnEvent : IBufferElementData` for the managed submission path; `AoeSpawnCommandData` is native-only.

### 1.4 Damage replay — `DamageReplayEvent`
`CombatPendingDamage` is renamed `DamageReplayEvent` (design §8.1 vocabulary). It keeps the **`TargetId` + `CombatFaction`** identity model (NOT an `Entity TargetProxy`; per D1 proxy entities are out of scope). The scope buffer `CombatDamageElement` and the flush job are **preserved** (a `DynamicBuffer` drained once per frame already provides the "frozen array, consumed once" semantics the contract asks for). The dispatch system is renamed `DamageDispatchBridge` and its role doc narrowed to "the only approved reader that crosses to managed target callbacks" (design §8.4).

### 1.5 Unified lifetime — `CombatLifetimeComponent`
New shared component in `System/Common/`:
```
// ECS Lifecycle: enableable common lifetime component; added at entity creation
// for finite-lifetime reusable entities; ENABLED on spawn for finite lifetimes,
// DISABLED for pulse AOEs (which are deactivated by collision the same tick);
// CombatLifetimeSystem counts it down and disables the entity's active tag on expiry.
public struct CombatLifetimeComponent : IComponentData, IEnableableComponent { public float Remaining; }
```
Replaces `ProjectileLifetimeComponent` and `AoeLifetimeComponent.RemainingLifetime`. The AoE `IsPulse` concept becomes "**`CombatLifetimeComponent` is disabled**" (component-state = behavior, R2): pulse AOEs are spawned with the lifetime component disabled, so the unified system skips them and the AoE collision system deactivates them as today.

---

## 2. Final system responsibilities

### Projectile spawn pipeline (replaces ChildSpawn + MultiExpand + SpawnSystem)
1. **`TimedProjectileSpawnSystem`** (`ISystem`, producer) — replaces `ProjectileChildSpawnSystem`. `IJobEntity` over child-spawner archetype; timed catch-up loop enqueues `ProjectileSpawnEvent` into the expansion system's `NativeQueue<ProjectileSpawnEvent>.ParallelWriter`. No ECB, no buffer append.
2. **`ProjectileSpawnExpansionSystem`** (`SystemBase`) — replaces `ProjectileMultiExpandSystem`. **Owns** `NativeQueue<ProjectileSpawnEvent> EventQueue` (Persistent). OnUpdate: complete producers; drain `EventQueue` + the scope's `ProjectileSpawnEvent` managed-submission buffer into one `NativeArray<ProjectileSpawnEvent>` (frozen); clear both; schedule the expansion `IJob`/parallel job that resolves count/spread/jitter/direction/velocity/bounds/render-Z/id and writes `ProjectileSpawnCommandData` into an output container (`NativeStream` or `NativeQueue`) exposed to apply (mirrors today's `PendingStream`).
3. **`ProjectileSpawnApplySystem`** (`SystemBase`) — replaces `ProjectileSpawnSystem`. Drains the command output → array; buckets by shape key `(faction, typeId, hasChildSpawner)`; reuses `WithDisabled<ProjectileActiveTag>` slots via the existing `IJobChunk`; cold-creates overflow via ECB. **The proven reuse/cold-create machinery from `ProjectileSpawnSystem` is kept almost verbatim** — only its input changes from dual-use elements to `ProjectileSpawnCommandData`.

### AoE spawn pipeline (replaces AoeSpawnSystem)
1. **`AoeSpawnExpansionSystem`** (`SystemBase`) — owns `NativeQueue<AoeSpawnEvent>`; drains queue + scope managed-submission buffer; resolves world bounds; emits `AoeSpawnCommandData` (1:1). Also emits the spawn-time VFX request (`Trigger=0`) that `AoeSpawnSystem` emits today.
2. **`AoeSpawnApplySystem`** — bucket by `(faction, typeId)`; reuse/cold-create. Body kept from `AoeSpawnSystem`, input changed to `AoeSpawnCommandData`.

> There is no `TimedAoeSpawnSystem` in this rewrite (no current behavior requires it; design §5.7 lists timed AoE as future). The pipeline shape supports adding one later without restructuring.

### Collision (final producer of typed consequence events — design §7.2)
`ProjectileCollisionSystem` / `AoeCollisionSystem` keep their detection + inline gate + source self-mutation. On a qualified hit they now write the **final typed** events directly:
- `ContactDamage` present → `DamageReplayEvent` (unchanged transport: `CombatPendingDamage`→renamed→`CombatDamageElement` flush).
- `ImpactProjectileSpawn` present → build a `ProjectileSpawnEvent` (the id-hash + direction-toward/away-target + request-build logic currently in `CombatSpawnConvertJob` moves into a shared helper, e.g. `ProjectileSpawnPipeline.BuildImpactProjectileEvent`) and enqueue it into `ProjectileSpawnExpansionSystem.EventQueue`.
- `ImpactAoeSpawn` / `AoeProjectileBurst` present → build an `AoeSpawnEvent` / `ProjectileSpawnEvent` and enqueue.
- VFX → unchanged (`VfxPendingSpawn` → `VfxStreamFlushJob`).

`CombatSpawnConvertJob` and `CombatPendingSpawn` are **deleted** once both collisions emit typed events.

### Damage dispatch
`DamageDispatchBridge` (renamed `CombatHitDispatchSystem`) — unchanged behavior. The single owner of the `CombatDamageElement` clear becomes one `OrderFirst` system (consolidate the current double-clear; see D-DAMAGE-CLEAR).

### Lifetime
`CombatLifetimeSystem` (`ISystem`, `System/Common/`) — `IJobEntity` over `(active tag, enabled CombatLifetimeComponent)` per domain (two queries or two scheduled jobs sharing the countdown), counts down, disables the active tag + `CombatRenderActiveTag`, emits despawn VFX (`Trigger=2`, area = `max(render.VisualScale.xy)` for both domains). `AoePulseVfxSystem` (`ISystem`, AoE-only) keeps the periodic pulse VFX (`Trigger=3`). `ProjectileLifetimeSystem` and `AoeLifetimeSystem` are deleted.

---

## 3. Final native-container ownership & phase rules (design §6)

| Container | Owner | Writers | Reader / finalize |
|---|---|---|---|
| `NativeQueue<ProjectileSpawnEvent>` | `ProjectileSpawnExpansionSystem` | `TimedProjectileSpawnSystem`, `ProjectileCollisionSystem`, `AoeCollisionSystem` (impact projectile / burst), via `.AsParallelWriter()` | expansion: complete → drain to `NativeArray` → `Clear()` |
| scope `DynamicBuffer<ProjectileSpawnEvent>` | scope entity (managed submission only) | `CombatRoot.Spawn` (main thread) | expansion: drain + `Clear()` (same finalize) |
| command output (`NativeStream`/`NativeQueue<ProjectileSpawnCommandData>`) | `ProjectileSpawnExpansionSystem` | expansion job | apply: complete → read by ranges → dispose/clear |
| `NativeQueue<AoeSpawnEvent>` + scope buffer | `AoeSpawnExpansionSystem` | collisions, `CombatRoot.Spawn` | expansion finalize |
| `CombatDamageElement` (scope buffer) | scope | collision flush job | `DamageDispatchBridge`: replay then `Clear()` |

Phase contract: no system reads a queue still being written; no system mutates command data after it is finalized into an array; producers always run before the matching expansion (enforced by §9 order). Containers are created/cleared/disposed by their owner only; cross-system access uses `World.GetExistingSystemManaged<T>()` (the established pattern from today's MultiExpand↔SpawnSystem coupling).

---

## 4. Rejected alternatives (summary; full text in 005)
- **Rename managed `ProjectileSpawnCommand`/`AoeSpawnCommand` to free the name for the ECS command** — rejected: broad churn across attack/skill code that is out of scope; used the `Data` suffix instead.
- **Per-shape command queue + per-shape apply system for every theoretical shape** (literal §5.3) — rejected as speculative; the design itself says "Start explicit. Generalize only after real duplication appears." The two real shapes (with/without child-spawner) are handled by the existing shape-keyed bucketing.
- **Migrate damage to `NativeQueue<DamageReplayEvent>` + proxy entities** — deferred: the scope buffer already gives frozen-once-per-frame semantics and atomic per-hit replay; proxy entities are a target-bridge rebuild that §1/§11 place out of scope.
- **Generic single `Active` flag replacing both domain active tags** — deferred: per-domain tags already satisfy §3's functional intent; merging is wide, risky churn with no capability gain.

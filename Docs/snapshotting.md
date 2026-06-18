# Snapshotting

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

## Summary

Combat spawn data is snapshotted before ECS simulation owns it. In-flight
projectiles and AOEs must not depend on live managed authoring objects,
ScriptableObjects, prefab components, Transforms, or target GameObjects.

Current combat flow uses plain-data snapshots:

- managed requests are converted into typed spawn events
- expansion systems convert events into one-entity commands
- apply systems initialize ECS components from commands
- collision systems read only ECS component snapshots and target proxy data
- collision systems emit typed plain-data consequence events
- damage is finalized and replayed through one controlled managed bridge

This is the core safety rule for projectile -> AOE, projectile -> projectile,
AOE -> projectile, stack-triggered AOE, and future chained effects.

## Current Data Levels

Managed request:

- `ProjectileSpawnRequest`
- `AoeSpawnRequest`

These are scene-side DTOs passed to `CombatRoot.Spawn`.

Spawn event:

- `ProjectileSpawnEvent`
- `AoeSpawnEvent`

Events are gameplay intent. A projectile event may describe multiplicity through
count, spread, jitter, speed, and base direction. Events can be submitted by
managed code through the shared scope buffer or by ECS producers through native
queues.

Spawn command:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`

Commands are allocation intent. A command describes exactly one ECS entity and
contains no volley multiplicity. Apply systems consume commands to reuse a
disabled `Active` slot or cold-create an overflow entity.

Runtime component snapshot:

- `ProjectileHitComponent`
- `ProjectileChildSpawnerComponent`
- `AoeHitSpawnComponent`
- `CombatHitPayload`
- impact and burst snapshot structs in `PlayGround.System.Common`

Collision consequence event:

- `DamageReplayEvent`
- `ProjectileSpawnEvent`
- `AoeSpawnEvent`
- `VfxPendingSpawn`

## Required Rules

- Snapshot payloads must contain only plain data: integers, floats, enums,
  small value structs, `Entity`, and ids.
- Snapshot payloads must not contain managed references, strings, GC handles,
  GameObjects, Transforms, Colliders, or ScriptableObjects.
- Collision systems must never call into authoring objects or managed target
  callbacks.
- Damage, spawn follow-ups, and VFX are separate typed paths.
- Only `DamageDispatchBridge` may read managed `TargetCompanion` references.
- Spawn follow-ups stay in ECS as typed spawn events and go through normal
  expansion/apply.

## Hit Payload

`CombatHitPayload` is shared by projectile and AOE domains:

```csharp
public struct CombatHitPayload
{
    public float DamageAmount;
    public float CritChance;
    public float CritMultiplier;
    public bool DirectDamageEnabled;
    public EntityId SourceNodeId;
    public CombatStatusEffectSnapshot StackEffect;
}
```

It carries direct damage, crit inputs, source-node identity, and optional stack
effect data. It is snapshotted before spawn and copied into ECS runtime
components.

## Projectile Runtime Snapshot

Projectile entities carry `ProjectileHitComponent`:

```csharp
public struct ProjectileHitComponent : IComponentData
{
    public int PierceRemaining;
    public float RepeatHitCooldownSeconds;
    public ProjectileHitPayload HitPayload;
}
```

`ProjectileHitPayload` wraps:

- `CombatHitPayload`
- optional `ProjectileImpactAoeSnapshot`
- optional `ProjectileImpactProjectileSnapshot`

When a projectile hit qualifies, `ProjectileCollisionSystem` may emit:

- `DamageReplayEvent`
- `AoeSpawnEvent` from `AoeSpawnPipeline.BuildImpactAoeEvent`
- `ProjectileSpawnEvent` from `ProjectileSpawnPipeline.BuildImpactProjectileEvent`
- `VfxPendingSpawn`

The collision system may disable the source projectile by disabling `Active`
when pierce is consumed.

## AOE Runtime Snapshot

AOE entities carry `AoeHitSpawnComponent`:

```csharp
public struct AoeHitSpawnComponent : IComponentData
{
    public CombatHitPayload HitPayload;
    public AoeProjectileBurstSnapshot ProjectileBurst;
}
```

When an AOE hit qualifies, `AoeCollisionSystem` may emit:

- `DamageReplayEvent`
- `ProjectileSpawnEvent` from `ProjectileSpawnPipeline.BuildBurstEvent`
- `VfxPendingSpawn`

Pulse AOEs disable `Active` after their one collision pass. Lingering AOEs keep
per-target contact gates and expire through `CombatLifetimeSystem`.

## Target Snapshot Model

Target collision data is no longer filled into per-scope target buffers for the
current projectile/AOE path. Targets are represented by proxy entities:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction`
- `TargetCompanion`

Player and mob roots push position and shape into their proxy in `Update()`.
Collision and tracking jobs read only unmanaged proxy data. Damage events carry
the target proxy `Entity`, and `DamageDispatchBridge` resolves the managed
companion during presentation replay.

`CombatTargetElement` remains in code for compatibility, but new projectile and
AOE work should use target proxy entities.

## Projectile Data Flow

```text
ProjectileSpawnRequest
  -> CombatRoot.Spawn
  -> ProjectileSpawnEvent on shared scope buffer
  -> ProjectileSpawnExpansionSystem
  -> ProjectileSpawnCommand
  -> BasicProjectileSpawnApplySystem or ChildSpawnerProjectileSpawnApplySystem
  -> Projectile ECS entity with snapshotted components
  -> ProjectileCollisionSystem
     -> DamageReplayEvent
     -> optional AoeSpawnEvent
     -> optional ProjectileSpawnEvent
     -> optional VfxPendingSpawn
  -> DamageFinalizeSystem
  -> DamageDispatchBridge
  -> ICombatTarget.ReceiveHits
```

Timed child projectiles follow the same event path:

```text
ProjectileChildSpawnerComponent
  -> TimedProjectileSpawnSystem
  -> ProjectileSpawnEvent
  -> ProjectileSpawnExpansionSystem
  -> ProjectileSpawnCommand
  -> projectile apply systems
```

## AOE Data Flow

```text
AoeSpawnRequest
  -> CombatRoot.Spawn
  -> AoeSpawnEvent on shared scope buffer
  -> AoeSpawnExpansionSystem
  -> AoeSpawnCommand
  -> AoeSpawnApplySystem
  -> AOE ECS entity with snapshotted components
  -> AoeCollisionSystem
     -> DamageReplayEvent
     -> optional ProjectileSpawnEvent
     -> optional VfxPendingSpawn
  -> DamageFinalizeSystem
  -> DamageDispatchBridge
  -> ICombatTarget.ReceiveHits
```

## Stack Effect Resolution

`CombatStatusEffectSnapshot` is part of `CombatHitPayload`. It is damage replay
data, not a cross-domain spawn payload.

Current stack-triggered AOE flow:

1. Collision emits `DamageReplayEvent` with `StackEffect`.
2. `DamageDispatchBridge` converts it to `CombatHitData`.
3. `MobRoot.ReceiveHit` or `ReceiveHits` applies stack state.
4. When the threshold triggers, `MobRoot` submits an `AoeSpawnRequest` through
   the player-faction `CombatRoot`.
5. The AOE then follows the normal AOE spawn pipeline.

This is still a managed target-side decision. It is intentionally separate from
collision. Future work may move high-volume stack aggregation into ECS.

## Damage Replay

Collision jobs enqueue `DamageReplayEvent` into
`DamageDispatchBridge.DamageQueue`. The event carries:

- target proxy entity
- hit position and direction
- hit kind
- damage amount
- crit chance and multiplier
- direct-damage flag
- source node id
- stack effect
- source id and type id

`DamageFinalizeSystem` runs after collision and before spawn expansion. It
completes producers, freezes queue contents into a native array, and clears the
queue.

`DamageDispatchBridge` runs in `PresentationSystemGroup`. It sorts finalized
events by target proxy, rolls crit on the main thread, builds `CombatHitData`,
resolves `TargetCompanion`, and calls `ICombatTarget.ReceiveHits` once per
target group.

Known limitation: damage is grouped for callback dispatch but is still emitted
as one event per qualifying hit before dispatch.

## Cross-Domain Spawn Rules

Cross-domain spawn data is plain data carried by snapshots:

- projectile impact AOE uses `ProjectileImpactAoeSnapshot`
- projectile impact projectile uses `ProjectileImpactProjectileSnapshot`
- AOE projectile burst uses `AoeProjectileBurstSnapshot`

Collision systems do not allocate those entities. They emit spawn events. The
normal expansion/apply path decides commands, reuse, and cold creation.

Keep recursive spawn bounded by snapshot shape. Do not add unbounded child lists
or managed callbacks to collision-time payloads.

## Memory And Performance Guidance

- Pack snapshot payloads tightly.
- Prefer ids and enum values over strings.
- Resolve templates before ECS hot paths.
- Keep collision jobs Burst-compatible.
- Keep spawn math in expansion systems.
- Keep entity allocation/reuse in apply systems.
- Avoid per-hit managed allocation.
- Use profiler counters before changing event transport.

## Testing Checklist

- Fire projectile with impact AOE snapshot; confirm AOE event reaches AOE
  expansion and materializes through AOE apply.
- Fire projectile with impact projectile snapshot; confirm projectile event
  reaches projectile expansion and materializes through projectile apply.
- Fire AOE with projectile burst snapshot; confirm projectile burst follows the
  projectile spawn pipeline.
- Fire projectile with stack effect; confirm stacks accumulate on target and
  threshold AOE uses managed target-side spawn.
- Mutate authoring data after firing; verify in-flight entities still use the
  original snapshot.
- Confirm simulation jobs do not read managed companions.
- Confirm damage replay resolves `TargetCompanion` only in `DamageDispatchBridge`.

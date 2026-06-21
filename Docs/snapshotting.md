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
    public StackEffectSnapshot StackEffect;
}
```

It carries direct damage, crit inputs, source-node identity, and the optional
single-level applied-stack payload. The stack payload is plain data resolved
before root spawn; in-flight entities never read authoring assets or registries.

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

`StackEffectSnapshot` is part of `CombatHitPayload`. It is a single-level
applied-stack payload, not damage replay data. It flows through:

- `AoeSpawnRequest`
- `AoeSpawnEvent`
- `AoeSpawnCommand`
- `AoeHitSpawnComponent.HitPayload`

Current stacking-skill direction:

1. A stacking skill compiles to an applicator snapshot plus a detonation
   snapshot. The debuff key is minted during runtime registration for that
   compiled stacking-skill instance; it is not authored and is not the
   detonation type id.
2. A root or hit-spawned applicator receives one `StackEffectSnapshot`.
   Spawn expansion and apply copy that payload without transformation.
3. Applicator collision keeps damage replay and stack accrual on separate typed
   paths. On hit it emits `StackApplyEvent` with target proxy, debuff key,
   threshold, lifetime refresh, one-stack contribution, and detonation snapshot.
4. `StackAccrualSystem` is the sole writer of each target proxy's
   `TargetStackEntry` buffer. Entries are keyed by debuff id and store count,
   summed damage/projectile/area contribution, lifetime remaining, and the
   detonation snapshot.
5. Each matching stack refreshes lifetime and adds its fire-time contribution.
   At threshold, accrual dispatches by detonation kind and clears the entry.
   AOE detonation emits one `AoeSpawnEvent` using summed damage and area.
   Projectile detonation emits one `ProjectileSpawnEvent` through the existing
   AOE projectile-burst path: `SummedProjectileCount` becomes event `Count`,
   and `SummedDamage` is total nova damage split across those projectiles. If
   lifetime lapses below threshold, the entry fizzles and is removed with no
   detonation.
6. Composition uses ordinary AOE hit-spawn snapshots carried on the detonation,
   so the next stacking skill's applicator is a new spawn with its own debuff
   key and payload.

Damage replay does not carry stacks. Managed target callbacks receive direct
damage data only; stack accrual is owned by ECS.

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
- stack projectile detonation also uses `AoeProjectileBurstSnapshot`

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
- Fire stacking applicators with AOE and projectile sources whose detonation is
  projectile; confirm both queue a projectile nova with summed count and total
  damage.
- Fire a stacking applicator; confirm the applied AOE/projectile entity carries
  one `StackEffectSnapshot` in its hit payload.
- Apply stacks below threshold and stop refreshing; confirm the target
  `TargetStackEntry` fizzles with no detonation.
- Apply mixed fire-time contributions to one debuff key; confirm threshold
  detonation uses the summed contribution and clears the entry.
- Mutate authoring data after firing; verify in-flight entities still use the
  original snapshot.
- Confirm simulation jobs do not read managed companions.
- Confirm damage replay resolves `TargetCompanion` only in `DamageDispatchBridge`.

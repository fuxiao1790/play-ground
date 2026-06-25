# Snapshotting

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This is the detailed snapshotting and spawn-safety ECS doc. Use
[index.md](./index.md) for the simulation overview and aspect map.

## Summary

Combat spawn data is snapshotted before ECS simulation owns it. In-flight
projectiles and AOEs must not depend on live managed authoring objects,
ScriptableObjects, prefab components, Transforms, or target GameObjects.

Current combat flow uses plain-data snapshots:

- managed requests are converted into typed spawn events
- timed-spawn templates are stored as spawn events in scope-owned registries
- expansion systems convert events into one-entity commands
- apply systems initialize ECS components from commands
- collision systems read only ECS component snapshots and target proxy data
- collision systems emit typed plain-data consequence events
- damage and status are finalized in ECS and bridged once per hit target

This is the core safety rule for projectile -> AOE, projectile -> projectile,
AOE -> projectile, AOE -> AOE, stack-triggered detonations, interval child
spawns, and future chained effects.

## Current Data Levels

Managed request:

- `ProjectileSpawnRequest`
- `AoeSpawnRequest`

These are scene-side DTOs passed to `CombatRoot.Spawn`.

Spawn event:

- `ProjectileSpawnEvent`
- `AoeSpawnEvent`

Events are gameplay intent. Events may carry multiplicity, spread, jitter,
movement, hit payloads, render data, and optional `TimedSpawnComponent` data.
Events can be submitted by managed code through the shared scope buffer, by ECS
producers through native queues, or by `TimedSpawnSystem` from the event-template
registry.

Spawn command:

- `ProjectileSpawnCommand`
- `AoeSpawnCommand`

Commands are allocation intent. A command describes exactly one ECS entity and
contains no volley multiplicity. Apply systems consume commands to reuse a
disabled `Active` slot or cold-create an overflow entity.

Runtime component snapshot:

- `ProjectileHitComponent`
- `AoeHitSpawnComponent`
- `TimedSpawnComponent`
- `TimedSpawnStateComponent`
- `CombatHitPayload`
- impact, burst, and AOE-on-hit snapshot structs in
  `PlayGround.System.Common`

Scope-owned timed-spawn template registry:

- `ProjectileSpawnTemplate`
- `AoeSpawnTemplate`
- `NativeHashMap<Hash128, ProjectileSpawnEvent>`
- `NativeHashMap<Hash128, AoeSpawnEvent>`

Collision consequence event:

- `CombatHitEvent`
- `ProjectileSpawnEvent`
- `AoeSpawnEvent`
- `VfxPendingSpawn`

Finalized presentation result:

- `CombatTickResult`

## Required Rules

- Snapshot payloads must contain only plain data: integers, floats, enums,
  small value structs, `Entity`, `Hash128`, and ids.
- Snapshot payloads must not contain managed references, strings, GC handles,
  GameObjects, Transforms, Colliders, or ScriptableObjects.
- Collision systems must never call into authoring objects or managed target
  callbacks.
- Damage, status, spawn follow-ups, and VFX are separate typed paths.
- Only `CombatApplyBridge` may read managed `TargetCompanion` references.
- Spawn follow-ups stay in ECS as typed spawn events and go through normal
  expansion/apply.
- Timed spawn must use stored spawn events as templates. Do not add separate
  template-data structs or template-to-event conversion paths.
- Recursive or large child-spawn behavior must be represented by event-template
  keys, not by embedding full nested interval-spawn snapshots in commands.

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
applied-stack payload. The stack payload is plain data resolved before root
spawn; in-flight entities never read authoring assets or registries.

## Events As Templates

Interval-spawn templates are stored as the existing spawn event types:

```csharp
public struct ProjectileSpawnTemplate : IComponentData
{
    public NativeHashMap<Hash128, ProjectileSpawnEvent> Map;
}

public struct AoeSpawnTemplate : IComponentData
{
    public NativeHashMap<Hash128, AoeSpawnEvent> Map;
}
```

The shared `CombatScope` entity owns both maps. `CombatScopeOwner` creates them
when the first `CombatRoot` binds the world and disposes them when the last root
releases the scope.

`CombatRoot.RegisterTimedSpawnTemplate(in ProjectileSpawnEvent)` and
`CombatRoot.RegisterTimedSpawnTemplate(in AoeSpawnEvent)` hash the stored event
content with `SpawnTemplateHash.Of`. The returned `Hash128` is copied into a
`TimedSpawnComponent.TemplateKey`.

Registry rules:

- the stored event is the template
- there is no separate `TemplateData` struct
- there is no template-to-event conversion step
- stored events are blittable and read by Burst jobs
- per-instance fields are left default in the stored event before hashing
- identical child behavior deduplicates to one map entry
- entries are never removed in v1

Per-instance fields stamped by `TimedSpawnSystem`:

- `Position`
- `Faction`
- `BaseProjectileId` or `AoeId`
- `JitterSeed`
- `DeterministicIdTickIndex`

## Timed Spawn Runtime

`TimedSpawnComponent` is the slim, self-describing interval-spawn config:

```csharp
public struct TimedSpawnComponent : IComponentData
{
    public CombatFaction Faction;
    public int SourceId;
    public IntervalChildKind ChildKind;
    public Hash128 TemplateKey;
    public float IntervalSeconds;
    public float IntervalJitterSeconds;
    public int JitterSeed;
}
```

Hot timer state is separate:

```csharp
public struct TimedSpawnStateComponent : IComponentData
{
    public float CooldownRemaining;
    public int TickIndex;
}
```

`TimedSpawnTag` marks the timed-spawning archetype. Apply systems add
`TimedSpawnTag`, `TimedSpawnComponent`, and `TimedSpawnStateComponent` to
projectile or lingering-AOE source archetypes when the command carries enabled
timed spawn.

One `TimedSpawnSystem` handles both projectile and lingering-AOE sources. It
queries active entities with:

- `Active`
- `CombatLifetimeComponent`
- `CombatKinematicsComponent`
- `TimedSpawnTag`
- `TimedSpawnComponent`
- `TimedSpawnStateComponent`

The system ticks cooldown, fetches the stored event by `TemplateKey`, stamps the
per-instance fields, and enqueues the existing event type into the existing
projectile or AOE expansion queue. The only domain switch is
`TimedSpawnComponent.ChildKind`, which chooses the destination event queue.

The tick loop must keep its safety guard:

- clamp interval advance to a positive minimum
- cap catch-up ticks per update
- stop spawning when lifetime has expired or faction is `None`

## Projectile Runtime Snapshot

Projectile spawn events and commands may carry:

- `ProjectileHitPayload`
- `ProjectileImpactAoeSnapshot`
- `ProjectileImpactProjectileSnapshot`
- `TimedSpawnComponent`

Projectile entities carry `ProjectileHitComponent`:

```csharp
public struct ProjectileHitComponent : IComponentData
{
    public int PierceRemaining;
    public float RepeatHitCooldownSeconds;
    public ProjectileHitPayload HitPayload;
}
```

When a projectile hit qualifies, `ProjectileCollisionSystem` may emit:

- `CombatHitEvent`
- `AoeSpawnEvent` from `AoeSpawnPipeline.BuildImpactAoeEvent`
- `ProjectileSpawnEvent` from `ProjectileSpawnPipeline.BuildImpactProjectileEvent`
- `VfxPendingSpawn`

The collision system may disable the source projectile by disabling `Active`
when pierce is consumed.

## AOE Runtime Snapshot

AOE spawn events and commands may carry:

- `CombatHitPayload`
- `AoeProjectileBurstSnapshot`
- `AoeOnHitSpawnSnapshot`
- `TimedSpawnComponent`

AOE entities carry `AoeHitSpawnComponent`:

```csharp
public struct AoeHitSpawnComponent : IComponentData
{
    public CombatHitPayload HitPayload;
    public AoeProjectileBurstSnapshot ProjectileBurst;
    public AoeOnHitSpawnSnapshot AoeSpawn;
}
```

When an AOE hit qualifies, AOE collision may emit:

- `CombatHitEvent`
- `ProjectileSpawnEvent` from `ProjectileSpawnPipeline.BuildBurstEvent`
- `AoeSpawnEvent` from `AoeSpawnPipeline.BuildOnHitAoeSpawnEvent`
- `VfxPendingSpawn`

Pulse AOEs disable `Active` after their one collision pass. Lingering AOEs keep
per-target contact gates and expire through `CombatLifetimeSystem`.

## Target Snapshot Model

Targets are represented by proxy entities:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction`
- `TargetHealth`
- `TargetCompanion`
- `TargetStackEntry` buffer

Player and mob roots push position and shape into their proxy in `Update()`.
Collision, tracking, damage apply, and status jobs read only unmanaged proxy
data. Hit events carry the target proxy `Entity`. `CombatApplyBridge` resolves
the managed companion during presentation replay.

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
     -> CombatHitEvent
     -> optional AoeSpawnEvent
     -> optional ProjectileSpawnEvent
     -> optional VfxPendingSpawn
  -> CombatApplyFinalizeSystem
  -> StatusProcessSystem
  -> CombatApplyBridge
  -> ICombatTarget.ReceiveCombatTick
```

Timed child spawns follow the same event path:

```text
ProjectileSpawnEvent or AoeSpawnEvent
  -> CombatRoot.RegisterTimedSpawnTemplate
  -> Hash128 TemplateKey
  -> TimedSpawnComponent on source entity
  -> TimedSpawnSystem
  -> ProjectileSpawnEvent or AoeSpawnEvent
  -> normal expansion/apply path
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
  -> AOE collision system
     -> CombatHitEvent
     -> optional ProjectileSpawnEvent
     -> optional AoeSpawnEvent
     -> optional VfxPendingSpawn
  -> CombatApplyFinalizeSystem
  -> StatusProcessSystem
  -> CombatApplyBridge
  -> ICombatTarget.ReceiveCombatTick
```

## Stack Effect Resolution

`StackEffectSnapshot` is part of `CombatHitPayload`. It is a single-level
applied-stack payload, not managed damage replay data.

Current stacking direction:

1. A normal skill set with `StackingSupport` compiles to
   `RuntimeStackingDetonation`. The debuff key is minted during runtime
   registration for that compiled detonation instance; it is not authored and is
   not the detonation type id.
2. A normal projectile or AOE applicator reaches that detonation through
   `StackTrigger` and receives one `StackEffectSnapshot`. Spawn expansion and
   apply copy that payload without transformation.
3. Applicator collision keeps direct health damage and stack accrual on one ECS
   hit path by emitting `CombatHitEvent` with target proxy, direct-damage data,
   source metadata, and stack snapshot.
4. `CombatApplyFinalizeSystem` buckets hits by target proxy, rolls crits,
   subtracts ECS-owned `TargetHealth`, writes `TargetStackEntry` buffers, and
   freezes one `CombatTickResult` per hit target.
5. `StatusProcessSystem` runs after finalize and before spawn expansion. It
   processes target stack buffers, decays or fizzles entries, and queues
   threshold AOE or projectile detonations for same-frame expansion.
6. Composition uses ordinary hit-spawn snapshots carried by runtime
   definitions. The next applicator is a new spawn with its own stack payload
   and detonation debuff key.

Managed target callbacks receive aggregated combat tick data and changed status
snapshots. Stack accrual and threshold detonation are owned by ECS.

## Damage And Status Finalization

Collision jobs enqueue `CombatHitEvent` into
`CombatApplyFinalizeSystem.HitQueue`. The event carries:

- target proxy entity
- hit kind
- damage amount
- crit chance and multiplier
- direct-damage flag
- hit position
- source node id
- source id and type id
- stack snapshot

`CombatApplyFinalizeSystem` runs after collision and before spawn expansion. It
completes producers, buckets hits by target proxy, rolls crits with deterministic
random state, sums damage per target, subtracts ECS-owned `TargetHealth`, accrues
status stacks, and freezes one `CombatTickResult` per hit target.

`CombatApplyBridge` runs in `PresentationSystemGroup`. It resolves
`TargetCompanion` and calls `ICombatTarget.ReceiveCombatTick` once per target
that received direct damage or changed status.

Known limitation: managed direct-damage replay no longer preserves one
`CombatHitData` per hit. A dense frame produces one `CombatTickResult` with
aggregate damage, hit count, crit count, health, and status ranges.

## Cross-Domain Spawn Rules

Cross-domain spawn data is plain data carried by snapshots or registry keys:

- projectile impact AOE uses `ProjectileImpactAoeSnapshot`
- projectile impact projectile uses `ProjectileImpactProjectileSnapshot`
- AOE projectile burst uses `AoeProjectileBurstSnapshot`
- AOE-on-hit spawn uses `AoeOnHitSpawnSnapshot`
- stack projectile detonation also uses `AoeProjectileBurstSnapshot`
- interval child spawn uses `TimedSpawnComponent.TemplateKey`

Collision and timed-spawn systems do not allocate spawned entities. They emit
spawn events. The normal expansion/apply path decides commands, reuse, and cold
creation.

Keep recursive spawn bounded by snapshot shape or by registered event-template
keys. Do not add unbounded child lists or managed callbacks to collision-time or
tick-time payloads.

## Memory And Performance Guidance

- Store interval-spawn behavior as deduplicated event templates keyed by
  `Hash128`.
- Keep per-source timed-spawn components slim.
- Keep hot timer state separate from immutable interval-spawn config.
- Pack snapshot payloads tightly.
- Prefer ids, enum values, and hashes over strings.
- Resolve templates before ECS hot paths.
- Keep collision and timed-spawn jobs Burst-compatible.
- Keep spawn math in expansion systems.
- Keep entity allocation/reuse in apply systems.
- Avoid per-hit managed allocation.
- Keep `AoeSpawnCommand` and other stream-written commands under native stream
  block limits.
- Use profiler counters before changing event transport.

## Testing Checklist

- Fire projectile with impact AOE snapshot; confirm AOE event reaches AOE
  expansion and materializes through AOE apply.
- Fire projectile with impact projectile snapshot; confirm projectile event
  reaches projectile expansion and materializes through projectile apply.
- Fire AOE with projectile burst snapshot; confirm projectile burst follows the
  projectile spawn pipeline.
- Fire AOE with AOE-on-hit snapshot; confirm child AOE follows the AOE spawn
  pipeline.
- Fire timed projectile source; confirm `TimedSpawnSystem` fetches the stored
  projectile event by `TemplateKey` and emits children at the configured
  interval.
- Fire timed lingering-AOE source; confirm `TimedSpawnSystem` fetches the stored
  projectile or AOE event by `TemplateKey`, emits children, and stops at expiry.
- Confirm identical interval child behavior deduplicates to one registry entry
  and differing behavior creates distinct keys.
- Confirm changing dynamic count recompiles to a new key and re-selecting a
  prior count reuses the old key.
- Confirm stored template events have per-instance fields default before hashing.
- Confirm `sizeof(AoeSpawnCommand) < 4096`.
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
- Confirm `TargetCompanion` is resolved only in `CombatApplyBridge`.

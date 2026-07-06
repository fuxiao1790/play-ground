# Spawn Template Registry

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

This document defines the spawn-template-registry contract for ECS combat
spawning. Use [index.md](./index.md) for the broader simulation overview.

## Summary

Combat spawn data is snapshotted before ECS simulation owns it. In-flight
projectiles and AOEs must not depend on live managed authoring objects,
ScriptableObjects, prefab components, Transforms, or target GameObjects.

The flow is plain-data end to end:

- managed requests become typed spawn events
- spawn template registries store spawn events for entities that can spawn
  other entities, so follow-up behavior is referenced by key instead of being
  embedded in every command
- expansion systems turn events into one-entity commands
- apply systems initialize ECS components from those commands
- collision systems read only ECS snapshots and target proxy data
- consequence events remain typed and plain-data
- damage and status are finalized in ECS and bridged once per hit target

This rule applies to projectile -> AOE, projectile -> projectile, AOE ->
projectile, AOE -> AOE, stack-triggered detonations, interval child spawns,
and future chained effects.

## Registry Concurrency Contract

The spawn-template registry is written only by **external spawns** â€?managed
gameplay code running in the GameObject `Update()` lifecycle, which executes
before the ECS simulation tick. The registry is never written from inside the
tick: no system, job, collision, status, or timed-spawn code adds or removes
entries.

Because all writes happen before the tick and entries are never removed in v1,
the registry is **immutable for the entire simulation tick**:

- it cannot rehash or reallocate mid-tick;
- every simulation job may take it `[ReadOnly]` and read it concurrently with no
  safety-system conflict;
- a stored template is fetched, copied to a local value, stamped with
  per-instance fields, and enqueued â€?the registry entry itself is never mutated.

Registration, including loadout recompiles, happens in managed land before the
next tick, so the write-only-external contract holds across equipment changes.
On-demand registration, if ever needed, must also occur in `Update()` (external,
pre-tick) â€?never from a job. This invariant is what makes registry reads safe in
collision and status jobs; it is load-bearing, not incidental.

## Unified Spawn Model

A follow-up spawn is just a spawn. The source â€?projectile impact, AOE on-hit,
stack detonation, interval tick â€?does not change the result: some projectiles or
AOEs are created. Every follow-up therefore reduces to one keyed spawn, not a
bespoke per-source snapshot.

A source entity carries only two fields per follow-up:

- **spawn kind** â€?projectile or AOE
- **spawn template id** â€?a `Hash128` key into the registry

The registry value is the spawn command template (the resolved spawn data). The
source holds the key; expansion dereferences it. This replaces the embedded
per-source snapshot structs (`AoeProjectileBurstSnapshot`,
`ProjectileImpactProjectileSnapshot`, `ProjectileImpactAoeSnapshot`,
`AoeOnHitSpawnSnapshot`) and removes the value-type cycle they forced
(`StackEffectSnapshot -> DetonationSnapshot -> AoeProjectileBurstSnapshot`): a key
reference cannot form a struct cycle. A stacking detonation is likewise just a
`(kind, key)` plus contribution; `StackEffectSnapshot` carries the detonation key,
not an embedded `DetonationSnapshot`.

### Events Carry No Data

Only the **command** holds spawn data. The **event** is a slim link into the
registry plus the per-instance frame:

- spawn kind + template key
- position, aim / base direction
- faction, source id, jitter seed, deterministic tick index
- contact-gate seed target (so an impact spawn does not re-hit the just-hit
  target)

Expansion is the single dereference-and-explode step: read the slim event, fetch
the template by key, apply the instance frame, and emit one command per spawned
entity. Multiplicity, spread, and jitter are template-level and read from the
registry during expansion â€?not carried on the event. This keeps everything
through native queues and scope buffers a tiny ref struct, with exactly one fat
data shape (the registry template) and one materialized shape (the command).

Every runtime spawn event references a registry entry, including the root cast.
There are no ad-hoc data-carrying events.

### Bounded Nesting

Spawn chains are bounded to **3 levels**: level 1 is the initial cast, level 2 is
the first trigger, level 3 is the second trigger. Because nesting is bounded and
fully authored, every template is enumerable and registered at compile time, so
the registry is complete before the first tick. The depth cap is enforced at
registration time.

## Current Data Levels

Managed request:

- `ProjectileSpawnRequest`
- `AoeSpawnRequest`

These are scene-side DTOs passed to `CombatRoot.Spawn`.

Spawn event:

- `ProjectileSpawnEvent`
- `AOE variant spawn event`

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

Scope-owned spawn template registry:

- `ProjectileSpawnTemplate`
- `AoeSpawnTemplate`
- `NativeHashMap<Hash128, ProjectileSpawnCommand>`
- `NativeHashMap<Hash128, AoeSpawnCommand>`

Collision consequence event:

- `CombatHitEvent`
- `ProjectileSpawnEvent`
- `AOE variant spawn event`
- `VfxPendingSpawn`

Finalized presentation result:

- `CombatTickResult`

## Required Rules

- Snapshot payloads may contain only plain data: integers, floats, enums,
  small value structs, `Entity`, `Hash128`, and ids.
- Snapshot payloads must not contain managed references, strings, GC handles,
  GameObjects, Transforms, Colliders, or ScriptableObjects.
- Collision systems must never call into authoring objects or managed target
  callbacks.
- Damage, status, spawn follow-ups, and VFX must stay on separate typed paths.
- Only `CombatApplyBridge` may read managed `TargetCompanion` references.
- Follow-up spawns stay in ECS as typed spawn events and flow through normal
  expansion and apply.
- Timed spawn must use stored spawn events as templates. Do not add separate
  template-data structs or template-to-event conversion paths.
- Recursive or large child-spawn behavior must be keyed by spawn-template
  reference, not embedded in commands.

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

Spawn template registries store command-shaped templates so entities that can
spawn other entities can reference them by key. This is the shared storage for
all follow-up spawn behavior â€?interval, on-hit, and detonation:

```csharp
public struct ProjectileSpawnTemplate : IComponentData
{
    public NativeHashMap<Hash128, ProjectileSpawnCommand> Map;
}

public struct AoeSpawnTemplate : IComponentData
{
    public NativeHashMap<Hash128, AoeSpawnCommand> Map;
}
```

The shared `CombatScope` entity owns both maps. `CombatScopeOwner` creates them
when the first `CombatRoot` binds the world and disposes them when the last root
releases the scope.

`CombatRoot.RegisterSpawnTemplate(in ProjectileSpawnCommand)` and
`CombatRoot.RegisterSpawnTemplate(in AoeSpawnCommand)` hash the normalized
command content with `SpawnTemplateHash.Of`. The returned `Hash128` is used as
the registry key and stored on whichever source carries the follow-up slot
(e.g., `TimedSpawnComponent.TemplateKey` or `OnHitSpawnRef.TemplateKey`).

Registry rules:

- the stored value is a **command-shaped template** â€?the same struct that
  expansion writes, with per-instance fields zeroed
- there is no separate `TemplateData` struct
- there is no event-to-command remap step; expansion stamps and explodes directly
- stored commands are blittable and readable by Burst jobs
- per-instance fields are left default before hashing
- identical follow-up behavior deduplicates to one map entry
- entries are never removed in v1

Per-instance fields stamped by expansion:

- `Position`
- `Faction`
- `ProjectileId` or `AoeId`
- `JitterSeed`
- `DeterministicIdTickIndex`
- `SeedContactGateTargetId` (for on-hit spawns)

## Timed Spawn Runtime

`TimedSpawnComponent` is the slim, self-describing interval-spawn config:

```csharp
public struct TimedSpawnComponent : IComponentData, IEnableableComponent
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

`TimedSpawnComponent` enabled state marks an interval-spawning source. Projectile
and lingering-AOE archetypes always contain `TimedSpawnComponent` and
`TimedSpawnStateComponent`; apply systems enable the component only when the
command carries timed spawn. Impact AOEs do not contain timed-spawn components.

One `TimedSpawnSystem` handles both projectile and lingering-AOE sources. It
queries active entities with:

- `Active`
- `CombatLifetimeComponent`
- `CombatKinematicsComponent`
- `TimedSpawnComponent`
- `TimedSpawnStateComponent`

The system ticks cooldown, fetches the stored event by `TemplateKey`, stamps the
per-instance fields, and enqueues the existing event type into the projectile or
AOE expansion queue. The only domain switch is
`TimedSpawnComponent.ChildKind`, which selects the destination queue.

The tick loop must keep these safety guards:

- clamp interval advance to a positive minimum
- cap catch-up ticks per update
- stop spawning when lifetime has expired or faction is `None`

## Projectile Runtime Snapshot

Projectile commands and component data carry:

- `ProjectileHitPayload` â€?hit payload with optional `OnHitSpawnRef (kind, key)`
- `TimedSpawnComponent` â€?optional interval-child spawn config

Projectile entities carry `ProjectileHitComponent`:

```csharp
public struct ProjectileHitComponent : IComponentData
{
    public int PierceRemaining;
    public float RepeatHitCooldownSeconds;
    public ProjectileHitPayload HitPayload;
}
```

`ProjectileHitPayload` wraps `CombatHitPayload` plus `OnHitSpawnRef`. The
`OnHitSpawnRef` is a `(kind, Hash128)` reference into the registry; it replaces
the former embedded `ProjectileImpactAoeSnapshot` and
`ProjectileImpactProjectileSnapshot`.

When a projectile hit qualifies, `ProjectileCollisionSystem` may emit:

- `CombatHitEvent`
- `ProjectileSpawnEvent` (slim link) when `OnHitSpawn.Kind == Projectile`
- `AOE variant spawn event` (slim link) when `OnHitSpawn.Kind == ImpactAoe/LingeringAoe`
- `VfxPendingSpawn`

The collision system may disable the source projectile by disabling `Active`
when pierce is consumed.

## AOE Runtime Snapshot

AOE commands and component data carry:

- `CombatHitPayload` â€?hit payload with optional stack effect
- `OnHitSpawnRef (kind, key)` â€?optional on-hit follow-up spawn reference
- `TimedSpawnComponent` â€?optional interval-child spawn config

AOE entities carry `AoeHitSpawnComponent`:

```csharp
public struct AoeHitSpawnComponent : IComponentData
{
    public CombatHitPayload HitPayload;
    public OnHitSpawnRef OnHitSpawn;
}
```

`OnHitSpawnRef` is a `(kind, Hash128)` reference into the registry; it replaces
the former embedded `AoeProjectileBurstSnapshot` and `AoeOnHitSpawnSnapshot`.

When an AOE hit qualifies, AOE collision may emit:

- `CombatHitEvent`
- `ProjectileSpawnEvent` (slim link) when `OnHitSpawn.Kind == Projectile`
- `AOE variant spawn event` (slim link) when `OnHitSpawn.Kind == ImpactAoe/LingeringAoe`
- `VfxPendingSpawn`

Pulse AOEs disable `Active` after their one collision pass. Lingering AOEs tick
from their own interval state and expire through `CombatLifetimeSystem`.

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
  -> ProjectileSpawnApplySystem
  -> Projectile ECS entity with snapshotted components
  -> ProjectileCollisionSystem
     -> CombatHitEvent
     -> optional AOE variant spawn event
     -> optional ProjectileSpawnEvent
     -> optional VfxPendingSpawn
  -> CombatApplyFinalizeSystem
  -> StatusProcessSystem
  -> CombatApplyBridge
  -> ICombatTarget.ReceiveCombatTick
```

Timed child spawns follow the same event path:

```text
ProjectileSpawnEvent or AOE variant spawn event
  -> CombatRoot.RegisterTimedSpawnTemplate
  -> Hash128 TemplateKey
  -> enabled TimedSpawnComponent on source entity
  -> TimedSpawnSystem
  -> ProjectileSpawnEvent or AOE variant spawn event
  -> normal expansion/apply path
```

## AOE Data Flow

```text
AoeSpawnRequest
  -> CombatRoot.Spawn
  -> AOE variant spawn event on shared scope buffer
  -> AOE spawn expansion systems
  -> AoeSpawnCommand
  -> ImpactAoeSpawnApplySystem or LingeringAoeSpawnApplySystem
  -> AOE ECS entity with snapshotted components
  -> AOE collision system
     -> CombatHitEvent
     -> optional ProjectileSpawnEvent
     -> optional AOE variant spawn event
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

Every follow-up spawn uses the same `(kind, Hash128)` reference into the
registry, regardless of the source:

- projectile on-hit â†?AOE: `ProjectileHitPayload.OnHitSpawn {Kind=Aoe, key}`
- projectile on-hit â†?projectile: `ProjectileHitPayload.OnHitSpawn {Kind=Projectile, key}`
- AOE on-hit â†?projectile burst: `AoeHitSpawnComponent.OnHitSpawn {Kind=Projectile, key}`
- AOE on-hit â†?AOE: `AoeHitSpawnComponent.OnHitSpawn {Kind=Aoe, key}`
- stack projectile detonation: `StackEffectSnapshot.DetonationKey` (Projectile kind)
- interval child spawn: `TimedSpawnComponent.TemplateKey`

Collision and timed-spawn systems emit slim `SpawnEvent` links (kind + key +
per-instance frame). The normal expansion/apply path dereferences the key,
stamps per-instance fields, and decides commands, reuse, and cold creation.

Recursive spawn is bounded by registered template keys, not by embedded
snapshot shape. A key reference cannot form a value-type cycle and cannot grow an
unbounded child list. Do not add managed callbacks to collision-time or tick-time
payloads.

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

- Fire projectile with `OnHitSpawn {Kind=Aoe, key}`; confirm AOE event reaches
  AOE expansion and materializes through AOE apply (registry dereference).
- Fire projectile with `OnHitSpawn {Kind=Projectile, key}`; confirm projectile
  event reaches projectile expansion and materializes through projectile apply.
- Fire AOE with `OnHitSpawn {Kind=Projectile, key}`; confirm projectile burst
  follows the projectile spawn pipeline.
- Fire AOE with `OnHitSpawn {Kind=Aoe, key}`; confirm child AOE follows the AOE
  spawn pipeline.
- Fire timed projectile source; confirm `TimedSpawnSystem` fetches the stored
  projectile command by `TemplateKey` and emits children at the configured
  interval.
- Fire timed lingering-AOE source; confirm `TimedSpawnSystem` fetches the stored
  projectile or AOE command by `TemplateKey`, emits children, and stops at expiry.
- Confirm identical follow-up behavior deduplicates to one registry entry and
  differing behavior creates distinct keys.
- Confirm changing dynamic count creates a new key and re-selecting a prior count
  reuses the old key.
- Confirm stored templates have per-instance fields zeroed before hashing.
- Confirm `sizeof(AoeSpawnCommand) < 4096`.
- Fire stacking applicators with AOE and projectile sources whose detonation is
  projectile; confirm both materialize a projectile nova with summed count and
  total damage.
- Fire a stacking applicator; confirm the applied AOE/projectile entity carries
  one `StackEffectSnapshot` in its hit payload.
- Apply stacks below threshold and stop refreshing; confirm the target
  `TargetStackEntry` fizzles with no detonation.
- Apply mixed fire-time contributions to one debuff key; confirm threshold
  detonation uses the summed contribution and clears the entry.
- Run a lingering-AOE â†?on-hit projectile â†?stack detonation chain; confirm
  three levels materialize correctly through the registry.
- Confirm the registry count is unchanged after a simulation tick.
- Mutate authoring data after firing; verify in-flight entities still use the
  original snapshot.
- Confirm simulation jobs do not read managed companions.
- Confirm `TargetCompanion` is resolved only in `CombatApplyBridge`.

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
projectile, AOE -> AOE, stack-triggered detonations, energy-driven child spawns,
and future chained effects.

## Registry Concurrency Contract

The spawn-template registry is written by managed gameplay code in GameObject
`Update()` before the ECS simulation tick, and by
`SpawnTemplateRefCountSystem` in `LateSimulationSystemGroup` after every
simulation job for that tick has completed. No job, collision, status, or
timed-spawn system mutates a template map.

The registry is therefore **immutable for the entire simulation tick**:

- it cannot rehash or reallocate mid-tick;
- every simulation job may take it `[ReadOnly]` and read it concurrently with no
  safety-system conflict;
- a stored template is fetched, copied to a local value, stamped with
  per-instance fields, and enqueued 鈥?the registry entry itself is never mutated.

Registration, including loadout recompiles, happens in managed land before the
next tick. Reclaim runs only after the tick, so `[ReadOnly]` concurrent registry
reads remain safe; this ordering is load-bearing, not incidental.

## Unified Spawn Model

A follow-up spawn is just a spawn. The source 鈥?projectile impact, AOE on-hit,
stack detonation, interval tick 鈥?does not change the result: some projectiles or
AOEs are created. Every follow-up therefore reduces to one keyed spawn, not a
bespoke per-source snapshot.

A source entity carries only two fields per follow-up:

- **spawn kind** 鈥?projectile or AOE
- **spawn template id** 鈥?a `Hash128` key into the registry

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
registry during expansion 鈥?not carried on the event. This keeps everything
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
- `TimedSpawnComponent`
- `TimedSpawnStateComponent`
- `CombatHitPayload`

Scope-owned spawn template registry:

- `ProjectileSpawnTemplate`
- `AoeSpawnTemplate`
- `NativeHashMap<Hash128, ProjectileSpawnCommand>`
- `NativeHashMap<Hash128, AoeSpawnCommand>`

Collision consequence event:

- `CombatHitEvent`
- `ProjectileSpawnEvent`
- `AOE variant spawn event`
- `CircularVfxSpawnRequest` / `TimedCircularVfxSpawnRequest` for AOE VFX (via `VfxEmit`)

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
- Only presentation bridges may read managed `TargetCompanion` references:
  `CombatApplyBridge` for combat results and `SpawnRejectionBridge` for rejected
  root-cast tokens.
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
all follow-up spawn behavior 鈥?interval, on-hit, and detonation:

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
(e.g., `TimedSpawnComponent.TemplateKey`).

Registry rules:

- the stored value is a **command-shaped template** 鈥?the same struct that
  expansion writes, with per-instance fields zeroed
- there is no separate `TemplateData` struct
- there is no event-to-command remap step; expansion stamps and explodes directly
- stored commands are blittable and readable by Burst jobs
- per-instance fields are left default before hashing
- identical follow-up behavior deduplicates to one map entry
- entries reclaim only when both managed owner count and entity instance count
  reach zero; pinned ad-hoc entries are never reclaimed

### Registry Lifetime

Registry identity is `(IntervalChildKind, Hash128)`. `OwnerCount` tracks managed
`RegisterSpawnTemplate` claims; `InstanceCount` tracks **live** entities carrying
that key. `Unregister` drops only one managed claim; it never erases a template
directly. The late-simulation sweep erases the counter entry and matching command
entry only when both counts are zero. `CombatRoot.Spawn` registrations are pinned
because no durable owner exists to release them.

`InstanceCount` moves only through events. Every spawn emits one acquire and every
despawn emits one release onto a shared `NativeQueue<SpawnTemplateRefDelta>`;
`SpawnTemplateRefCountSystem` drains that queue and is the only code that touches a
count. Spawn-apply, collision, and lifetime systems hold a `ParallelWriter` and
nothing more. Both halves go through `SpawnTemplateRefEmit`, the single definition of
which keys each domain carries, so acquire and release cannot disagree.

Two rules keep the accounting exact:

- Acquire is gated on the slot actually going live. An impact AOE with nothing to
  collide against is materialized inactive and never reaches a death site, so it must
  not claim a reference.
- Any job that reads `TimedSpawnComponent` to release its key must declare
  `[WithPresent(typeof(TimedSpawnComponent))]`. The component is enableable and
  disabled on non-timed sources, so an ordinary `All` match would silently drop every
  non-timed projectile from collision and expiry. When the job is scheduled against an
  explicit `EntityQuery` rather than the generated one, the attribute does not apply and
  the builder needs `.WithPresent<TimedSpawnComponent>()` as well — otherwise scheduling
  throws "the query must contain all the components required for `Execute()` to run".

`CombatPoolCleanupSystem` takes no part in this: it destroys already-disabled slots,
which released their keys when they died.

Per-instance fields stamped by expansion:

- `Position`
- `Faction`
- `ProjectileId` or `AoeId`
- `JitterSeed`
- `DeterministicIdTickIndex`
- `SeedContactGateTargetId` (for on-hit spawns)

## Timed Spawn Runtime

`TimedSpawnComponent` is the slim, self-describing energy-accrual config:

```csharp
public struct TimedSpawnComponent : IComponentData, IEnableableComponent
{
    public CombatFaction Faction;
    public int SourceId;
    public IntervalChildKind ChildKind;
    public Hash128 TemplateKey;
    public float EnergyPerSecond;
    public float EnergyThreshold;
    public int JitterSeed;
}
```

Hot timer state is separate:

```csharp
public struct TimedSpawnStateComponent : IComponentData
{
    public float EnergyAccumulated;
    public int TickIndex;
}
```

`TimedSpawnComponent` enabled state marks an energy-spawning source. Projectile
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

The system adds `EnergyPerSecond * deltaTime` to the source's empty-on-enable
energy accumulator. When energy reaches the next threshold, it consumes that
threshold, fetches the stored event by `TemplateKey`, stamps the per-instance
fields, and enqueues the existing event type into the projectile or AOE
expansion queue. The only domain switch is
`TimedSpawnComponent.ChildKind`, which selects the destination queue.

The tick loop must keep these safety guards:

- floor each threshold to a positive minimum
- cap emissions per update at 256
- stop spawning when lifetime has expired, faction is `None`, or rate is not positive

## Projectile Runtime Snapshot

Projectile commands and component data carry:

- `ProjectileHitPayload` 鈥?wraps `CombatHitPayload`
- `TimedSpawnComponent` 鈥?optional interval-child spawn config
- `LaunchAimMode` / `LaunchAimRange` 鈥?trigger-authored launch-aim policy,
  copied at compile time onto a trigger's projectile target only (never the
  root); template-level, not per-instance, so it participates in
  `SpawnTemplateHash` like `Speed` or `ContinuousCollision`. See
  [Launch Aim](../simulation/projectile-system.md#launch-aim).

Projectile entities carry `ProjectileHitComponent`:

```csharp
public struct ProjectileHitComponent : IComponentData
{
    public int PierceRemaining;
    public float RepeatHitCooldownSeconds;
}
```

When a projectile hit qualifies, either projectile collision system may emit
(both route through the shared `ProjectileHitEmission` helpers, so the lane does
not change what is emitted):

- `CombatHitEvent`

The collision system may disable the source projectile by disabling `Active`
when pierce is consumed.

## AOE Runtime Snapshot

AOE commands and component data carry:

- `CombatHitPayload` 鈥?hit payload with optional stack effect
- `TimedSpawnComponent` 鈥?optional interval-child spawn config

When an AOE hit qualifies, AOE collision may emit:

- `CombatHitEvent`
- `CircularVfxSpawnRequest` / `TimedCircularVfxSpawnRequest` (via `VfxEmit`)

Pulse AOEs disable `Active` after their one collision pass. Lingering AOEs tick
from their own interval state and expire through `CombatLifetimeSystem`.

## Target Snapshot Model

Targets are represented by proxy entities:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction`
- `Health`
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
  -> ProjectileDiscreteSpawnApplySystem   (ContinuousCollision == 0)
     or ProjectileContinuousSpawnApplySystem (ContinuousCollision != 0)
  -> Projectile ECS entity with snapshotted components
  -> ProjectileDiscreteCollisionSystem or ProjectileContinuousCollisionSystem
     -> CombatHitEvent
     -> optional AOE variant spawn event
     -> optional ProjectileSpawnEvent
  -> CombatApplyFinalizeSingleSystem
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
     -> optional CircularVfxSpawnRequest / TimedCircularVfxSpawnRequest
  -> CombatApplyFinalizeSingleSystem
  -> CombatApplyBridge
  -> ICombatTarget.ReceiveCombatTick
```

For stack payloads, finalization writes `TargetStackEntry`. On the next
simulation update, `StatusProcessSystem` evaluates that entry and may enqueue a
projectile or AOE detonation event before spawn expansion.

## Stack Effect Resolution

`StackEffectSnapshot` is part of `CombatHitPayload`. It is a single-level
applied-stack payload, not managed damage replay data.

Current stacking direction:

1. `StackTrigger` compiles its normal target skill set, wraps that result in a
   `RuntimeStackingDetonation`, and copies the link's `stackThreshold`,
   `debuffLifetimeSeconds`, and `stacksPerHit`. The debuff key is minted during
   runtime registration for that compiled detonation instance; it is not
   authored and is not the detonation type id.
2. The source projectile, AOE, or targeted applicator receives one
   `StackEffectSnapshot`. Spawn expansion and apply copy that payload without
   transformation.
3. Applicator collision keeps direct health damage and stack accrual on one ECS
   hit path by emitting `CombatHitEvent` with target proxy, direct-damage data,
   source metadata, and stack snapshot.
4. `CombatApplyFinalizeSingleSystem` buckets hits by target proxy, rolls crits,
   subtracts ECS-owned `Health`, writes target-local `TargetStackEntry` buffers
   keyed by `DebuffKey`, refreshes their expiry deadline, and
   freezes one `CombatTickResult` per hit target. It also accumulates
   `SummedDamage`, `SummedProjectileCount`, and `SummedArea`, but current
   detonation processing does not consume those totals.
5. On the next simulation update, `StatusProcessSystem` runs before hit
   finalization and spawn expansion. It expires stale entries and queues one
   AOE or projectile detonation per full threshold banked, preserving a
   sub-threshold remainder. Stacks accrued by current-update finalization are
   therefore not eligible until the next update. Emitted events carry the
   registered detonation template key, whose stored values determine damage,
   count, area, and other spawn behavior.
6. Composition uses ordinary hit-spawn snapshots carried by runtime
   definitions. The next applicator is a new spawn with its own stack payload
   and detonation debuff key.

Managed target callbacks receive aggregated combat tick data and changed status
snapshots. Stack accrual and threshold detonation are owned by ECS.
`StackEffectSnapshot.Enabled` requires `Lifetime > 0`; consequently,
`debuffLifetimeSeconds = 0` disables the stack effect.

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
random state, sums damage per target, subtracts ECS-owned `Health`, accrues
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

- stack projectile detonation: `StackEffectSnapshot.DetonationKey` (Projectile kind)
- energy child spawn: `TimedSpawnComponent.TemplateKey`

Collision and timed-spawn systems emit slim `SpawnEvent` links (kind + key +
per-instance frame). The normal expansion/apply path dereferences the key,
stamps per-instance fields, and decides commands, reuse, and cold creation.

Recursive spawn is bounded by registered template keys, not by embedded
snapshot shape. A key reference cannot form a value-type cycle and cannot grow an
unbounded child list. Do not add managed callbacks to collision-time or tick-time
payloads.

## Memory And Performance Guidance

- Store energy-driven child behavior as deduplicated event templates keyed by
  `Hash128`.
- Keep per-source timed-spawn components slim.
- Keep hot energy state separate from immutable energy-spawn config.
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
  projectile; confirm both materialize a projectile nova using count and damage
  from the registered projectile template.
- Fire a stacking applicator; confirm the applied AOE/projectile entity carries
  one `StackEffectSnapshot` in its hit payload.
- Apply stacks below threshold and stop refreshing; confirm the target
  `TargetStackEntry` fizzles with no detonation.
- Apply mixed fire-time contributions to one debuff key; confirm contribution
  totals accumulate on `TargetStackEntry` but detonation still uses the
  registered template values.
- Author `debuffLifetimeSeconds = 0`; confirm `StackEffectSnapshot` is disabled
  and no target stack entry is accrued.
- Run a lingering-AOE 鈫?on-hit projectile 鈫?stack detonation chain; confirm
  three levels materialize correctly through the registry.
- Confirm the registry count is unchanged after a simulation tick.
- Register identical content twice, unregister one owner, and confirm the entry
  survives. Release the final owner and confirm it reclaims after the sweep.
- Confirm a released child template survives while an in-flight entity still carries
  its key, then reclaims once that entity dies.
- Confirm a full spawn/expire lifecycle returns every instance count to zero.
- Confirm non-timed projectiles still collide and still expire after any change to a
  job that reads `TimedSpawnComponent` for release.
- Confirm an impact AOE materialized inactive claims no reference.
- Confirm repeated identical ad-hoc `CombatRoot.Spawn` calls keep one pinned entry.
- Mutate authoring data after firing; verify in-flight entities still use the
  original snapshot.
- Confirm simulation jobs do not read managed companions.
- Confirm `TargetCompanion` is resolved only by presentation bridges.

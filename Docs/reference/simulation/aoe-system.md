# AOE System

All docs in `Docs/` are design references. They describe current implementation
intent and should be checked against code before large changes.

This is the detailed AOE ECS doc. Use [index.md](./index.md) for the simulation
overview and aspect map.

## Summary

AOEs are high-count combat entities simulated with Unity Entities/DOTS. They
share the same combat bridge, target proxy model, damage transport, render path,
VFX path, and generic lifetime model as projectiles.

Current implementation is built around these rules:

- `CombatRoot` is the scene-object bridge and authoring owner for both
  projectiles and AOEs.
- One player-faction root and one mob-faction root share a single ref-counted
  ECS world and a single shared `CombatScope` entity.
- `CombatFaction` separates player-faction and mob-faction data.
- `ImpactAoeSpawnEvent` and `LingeringAoeSpawnEvent` are spawn intent.
- `AoeSpawnCommand` is one resolved AOE entity.
- Runtime reuse is based on the generic enableable `Active` component.
- AOE collision reads ECS target proxy entities.
- Damage returns to MonoBehaviours only through `DamageDispatchBridge`.

Primary uses:

- player AOEs hitting mobs
- mob AOEs hitting the player
- projectile impact explosions
- stack-triggered explosions
- lingering fields with per-AOE tick intervals
- AOE projectile bursts
- batched AOE rendering and VFX

Non-goals:

- live trigger callbacks as the authoritative damage path
- one global damage authority that discovers all scene objects
- managed target access from AOE simulation jobs

## Main Files

- `Assets/Scripts/System/Aoes/AoeSpawnPipeline.cs`: `ImpactAoeSpawnEvent`,
  `LingeringAoeSpawnEvent`, `AoeSpawnCommand`, and AOE variant helpers.
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`: contains
  `ImpactAoeSpawnExpansionSystem`, `LingeringAoeSpawnExpansionSystem`, and the
  shared `AoeExpansionCore` that drains AOE events and writes resolved commands.
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`: contains impact and
  lingering apply systems that top up and reuse disabled AOE entities.
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`: target proxy broad
  phase, narrow-phase collision, damage events, child spawn events, VFX events,
  and impact deactivation.
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`: lingering AOE
  tick interval countdown plus target proxy collision and consequence events.
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs`: periodic pulse VFX for
  lingering AOEs.
- `Assets/Scripts/System/Aoes/AoeEcsComponents.cs`: AOE identity, collision
  active tag, hit interval state, hit-spawn snapshot, area, and pulse VFX data.
- `Assets/Scripts/System/Aoes/AoeRuntimeEvents.cs`: managed AOE spawn request and
  counters.
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry.
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`: native hit queue
  finalize plus managed replay into `ICombatTarget.ReceiveHits`.

## Runtime Ownership

`CombatRoot` owns AOE authoring and bridge work:

- register `AoeTypeDefinition`
- validate AOE type ids and spawn geometry
- build AOE render resources
- append `ImpactAoeSpawnEvent` or `LingeringAoeSpawnEvent` values to the shared
  scope buffer for managed submissions
- expose the target registry for its faction

AOE ECS systems own:

- spawn event expansion
- slot reuse and cold creation
- lifetime expiry
- per-AOE hit interval maintenance
- collision and consequence event emission
- batched render matrix preparation

The current scope model is one shared `CombatScope` entity for all combat roots.
AOE systems must use `AoeTag` and `CombatFaction`; scope membership alone does
not identify an AOE domain or faction.

## Target Proxy Bridge

AOE collision uses the same target proxy bridge as projectile collision.

Target proxies carry:

- `TargetProxyTag`
- `TargetPosition`
- `TargetCollisionShape`
- `TargetFaction`
- managed `TargetCompanion`

Simulation reads only the unmanaged proxy components. The managed companion is
only read by `DamageDispatchBridge` during presentation replay.

Player and mob roots push their proxy position and collision shape in `Update`
and delete dead proxies in `LateUpdate`. This keeps proxy state available during
the simulation frame while avoiding unresolved damage events against already
destroyed proxy entities.

## Spawn Pipeline

`ImpactAoeSpawnEvent` and `LingeringAoeSpawnEvent` are intent. They can come
from:

- `CombatRoot.Spawn(AoeSpawnRequest)`
- projectile impact AOE snapshots
- stack-triggered managed spawns through mob/player hit handling

`AoeSpawnCommand` is the AOE command shape used by the spawn-template registry
and by apply. Before expansion it can carry fan-out behavior such as
`EchoCount`, `ScatterRadius`, and `JitterSeed`. After expansion each written
command represents one resolved AOE entity with final position, bounds, type id,
lifetime, repeat cooldown, hit payload, render data, and optional projectile
burst snapshot.

Current flow:

1. Managed code appends the variant event to the shared scope buffer, or ECS
   producers enqueue events into the matching AOE expansion system queue.
2. `ImpactAoeSpawnExpansionSystem` and `LingeringAoeSpawnExpansionSystem` drain
   their native event queues and matching scope buffers.
3. Expansion fans `EchoCount` copies, scatters each copy inside
   `ScatterRadius` using a deterministic random disk seeded by `JitterSeed`,
   computes per-copy bounds, and writes one `AoeSpawnCommand` per copy to the
   impact or lingering command container.
4. `ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem` read their
   command containers and count reusable AOE slots with `WithDisabled<Active>()`.
5. If disabled slots are short of demand,
   `SpawnPoolTopUp.EnsureDisabledSlots` cold-creates the deficit with
   `EntityManager.CreateEntity` and disables `Active` on each. This structural
   change runs before chunk arrays and type handles are fetched.
6. Each apply system then captures its disabled chunks and schedules one
   single-threaded Burst reuse job with one command cursor.
7. Reused slots are reset in query chunk order, so on a normal frame the reuse
   pass covers every command and the top-up count is what signals pool
   shortage.

AOE uses the same event-to-command fan-out contract as projectiles: spawn
events carry intent, expansion owns multiplicity and deterministic variation,
and apply only materializes already-resolved single-entity commands.

The impact-vs-lingering variant is chosen at authoring from child lifetime:
`Lifetime > 0` means lingering AOE, otherwise impact AOE. That variant is
carried on `IntervalChildKind` / `StackDetonationKind`, so collision, timed
spawn, and status producers route by kind without looking up templates.

## Entity Data And Reuse

There are two AOE archetypes:

- Impact AOE: lean one-shot area, no `LingeringAoeTag`, no
  `CombatLifetimeComponent`, no
  `AoePulseVfxComponent`, and no timed-spawn data.
- Lingering AOE: finite-lifetime area with `LingeringAoeTag`,
  `CombatLifetimeComponent`,
  `AoePulseVfxComponent`, `TimedSpawnComponent`, and `TimedSpawnStateComponent`.

All AOE entities carry:

- `AoeTag`
- `AoeIdentityComponent`
- generic `Active`
- `CombatKinematicsComponent`
- `CombatCollisionComponent`
- `CombatCollisionActiveTag`
- `ArmingTag`
- `CombatArmingComponent`
- `AoeHitGateComponent`
- `AoeHitSpawnComponent`
- `AoeAreaComponent`
- common render components

`LingeringAoeTag` is the impact-vs-lingering discriminator. Timed spawn is an
enableable bit on lingering AOEs only. This removes the former non-timed vs
timed-lingering archetype split while keeping impact chunks small.

Runtime despawn disables `Active` through the shared `CombatDeathUtility.Kill`
helper; sprite visibility derives from `Active`, so there is no separate render
gate. Entities remain available for reuse until the owning `CombatRoot` tears down
its faction data.

`CombatCollisionActiveTag` is separate from `Active`. It lets visual-only AOEs stay
active/rendered while collision skips them. It is the same generic collision gate
projectiles use; the AOE collision queries discriminate via `AoeTag`.

`ArmingTag` + `CombatArmingComponent` provide the optional `ArmSeconds`
initial-delay pause (see Arming in
[project-aoe-system-common.md](./project-aoe-system-common.md)). Impact AOEs, which
have no lifetime and die only via their one-shot collision pass, simply telegraph
during arming and perform that pass on the armed frame.

AOE reuse is single-cursor and deterministic per pool. Impact and lingering
reuse jobs scan disabled chunks in query order and consume commands in
command-list order. Cold creation runs before the reuse job rather than after
it, topping the pool up to demand, so the reuse count equals the command count
on a normal frame and the top-up count is what indicates true pool shortage for
that AOE archetype.

## Damage Timing

Impact AOE:

- `LingeringAoeTag` and `CombatLifetimeComponent` are absent.
- Collision runs once.
- The AOE deactivates after that collision pass.

Lingering AOE:

- `LingeringAoeTag` is present.
- `CombatLifetimeComponent` stores remaining lifetime.
- `CombatLifetimeSystem` expires it when remaining time reaches zero.
- Collision can hit immediately.
- `AoeHitGateComponent.Remaining` prevents another collision pass until the
  AOE tick interval expires.

There is no global AOE tick. Repeat timing belongs to each lingering AOE.

## Collision And Consequences

AOE collision systems own hit qualification and consequence emission. They may:

- query target proxy data
- build occupied target cells keyed by `TargetFaction`
- perform bounds and narrow-phase checks
- de-dup targets within one collision pass
- disable impact AOEs after their one collision pass
- emit plain data events for damage, projectile bursts, and VFX

It may not:

- call managed target callbacks
- spawn projectiles directly
- instantiate visual effects
- read `TargetCompanion`

Accepted hits can produce:

- `DamageReplayEvent` into `DamageDispatchBridge.DamageQueue`
- `ProjectileSpawnEvent` into projectile expansion for AOE projectile bursts
- `CircularVfxSpawnRequest` / `TimedCircularVfxSpawnRequest` into the per-shape VFX queues via `VfxEmit`

Damage is finalized by `DamageFinalizeSystem` before spawn expansion. Managed
replay runs later in `DamageDispatchBridge` during `PresentationSystemGroup`.

## Projectile Burst From AOE

AOEs can carry an `AoeProjectileBurstSnapshot` in `AoeHitSpawnComponent`.
On an accepted AOE hit, the AOE collision systems convert that snapshot into a
`ProjectileSpawnEvent` by calling `ProjectileSpawnPipeline.BuildBurstEvent`.
The projectile expansion and apply systems then handle volley expansion, reuse,
and cold creation.

This keeps AOE collision as an event producer, not an entity allocator.

## Lifetime And Pulse VFX

`CombatLifetimeSystem` handles lingering AOE expiry with the same common
`CombatLifetimeComponent` used by projectiles. When lifetime expires, it calls
`CombatDeathUtility.Kill` to disable `Active` (and `CombatCollisionActiveTag`) and
emit expire VFX.

`AoePulseVfxSystem` handles interval-based pulse VFX for active lingering AOEs.
It is separate from hit qualification and from lifetime expiry.

## Rendering And VFX

AOE visuals use the same shared-atlas rendering path as projectiles:

- `CombatRoot` builds render resources from `AoeTypeDefinition`,
  registering each kind's sprite with the shared
  `CombatRenderResourceRegistry` (one `SpriteAtlas`-backed texture, one mesh,
  one material for every kind).
- AOE entities carry common render components and faction/type shared
  components.
- `CombatRenderPrepareSystem` writes object matrices.
- `CombatBatchedRenderSystem` submits instances in `PresentationSystemGroup`,
  drawing every active kind together via the shared atlas instead of one
  batch per kind.

AOE gameplay does not depend on live visual GameObjects.

VFX requests flow as data:

- collision, lifetime, arming, and pulse call `VfxEmit.Enqueue`, which decodes the
  graph's data shape from the id and writes `CircularVfxSpawnRequest` or
  `TimedCircularVfxSpawnRequest` to the matching per-shape `NativeQueue`
- `CombatAoeVfxDispatchSystem` completes producers, buckets each shape by graph id,
  and dispatches through `CombatVfxRoot`

## Current Frame Order

Important simulation ordering:

1. `CombatLifetimeSystem` expires projectile and AOE lifetime.
2. `AoePulseVfxSystem` emits periodic pulse VFX for lingering AOEs.
3. Projectile tracking, continuous origin capture, movement, and contact gates
   run, then both the discrete and continuous projectile collision systems.
4. AOE collision systems emit damage, projectile spawn, AOE spawn, and VFX events.
6. `DamageFinalizeSystem` freezes the native damage queue.
7. `ProjectileSpawnExpansionSystem`, `ImpactAoeSpawnExpansionSystem`, and
   `LingeringAoeSpawnExpansionSystem` drain events and produce commands.
8. `ProjectileDiscreteSpawnApplySystem`, `ProjectileContinuousSpawnApplySystem`,
   `ImpactAoeSpawnApplySystem`, and `LingeringAoeSpawnApplySystem` top up their
   own pool and reuse slots.
9. `CombatRenderPrepareSystem` prepares render matrices.
10. Presentation systems dispatch damage, VFX, and render batches.

AOEs spawned/reused by apply systems do not collide until the next simulation
update because apply runs after collision.

## Authoring Notes

Current AOE authoring uses:

- `AoeSkill`/`LingeringAoeSkill` assets (embedding `AoeDefinition`/`LingeringAoeDefinition`)
- `AoeTypeDefinition`
- `BasicAoePrefab`, `LingeringAoePrefab`, or other validator prefabs
- `AoeSpawnGeometry` resolved before ECS receives the spawn event
- `AoeSpawnRequest` for managed spawn submission

AOE template prefabs provide visual and collider authoring data, but runtime
AOE gameplay uses ECS components. Do not add live trigger damage behavior to AOE
prefabs as the authoritative path.

`CombatRoot.RegisterConfig` and `CombatRoot.RegisterType` assign runtime type
ids and build render resources. `CombatRoot.Spawn(AoeSpawnRequest)` validates
the type id and resolved geometry before appending an impact or lingering AOE
spawn event.

## Stack-Triggered AOE

Stack-triggered AOE remains above the AOE runtime. For example, `MobRoot`
receives hit data, applies stack state, and when a threshold triggers it submits
an `AoeSpawnRequest` through the player-faction `CombatRoot`.

The AOE runtime only materializes and resolves the spawned area. It does not own
the gameplay decision that a stack threshold should create an explosion.

## Performance Notes

Current performance-sensitive choices:

- no one GameObject per AOE
- no live trigger callback hit path
- proxy targets instead of collider reads in simulation
- `Active` enable/disable reuse
- target spatial hashing in collision
- native queues/lists for damage, spawn, and VFX events
- one single-threaded Burst apply job per AOE reuse pool
- batched render submission

Revisit only with profiling:

- AOE spatial hash cell size
- dead-slot scan cost in spawn apply
- per-hit damage replay volume
- pulse VFX density and budgets

## Known Gaps

- Damage replay is still one event per qualifying hit before
  `DamageDispatchBridge` groups by target for callback dispatch.
- Crit rolling currently happens in the managed bridge.
- Dedicated AOE stress scenes and hard pass/fail thresholds are still limited.

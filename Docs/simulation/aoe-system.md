# AOE System

All docs in `Docs/` are design references. They describe current implementation
intent and should be checked against code before large changes.

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
- `AoeSpawnEvent` is spawn intent.
- `AoeSpawnCommand` is one resolved AOE entity.
- Runtime reuse is based on the generic enableable `Active` component.
- AOE collision reads ECS target proxy entities.
- Damage returns to MonoBehaviours only through `DamageDispatchBridge`.

Primary uses:

- player AOEs hitting mobs
- mob AOEs hitting the player
- projectile impact explosions
- stack-triggered explosions
- lingering fields with per-target repeat gates
- AOE projectile bursts
- batched AOE rendering and VFX

Non-goals:

- live trigger callbacks as the authoritative damage path
- one global damage authority that discovers all scene objects
- managed target access from AOE simulation jobs

## Main Files

- `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs`: `AoeSpawnEvent`,
  `AoeSpawnCommand`, and impact AOE event helpers.
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`: drains AOE events and
  writes resolved commands.
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`: reuses disabled AOE
  entities or cold-creates overflow.
- `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`: target proxy broad phase,
  narrow-phase collision, contact gates, damage events, projectile burst events,
  VFX events, and pulse deactivation.
- `Assets/Scripts/System/Aoe/AoeContactGateSystem.cs`: per-target repeat-hit
  gate expiry.
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`: periodic pulse VFX for
  lingering AOEs.
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`: AOE identity, collision
  active tag, hit gate, hit-spawn snapshot, area, contact gate, and pulse VFX
  data.
- `Assets/Scripts/System/Aoe/AoeConfig.cs`: ScriptableObject authoring for AOE
  type definitions.
- `Assets/Scripts/System/Aoe/AoeRuntimeEvents.cs`: managed AOE spawn request and
  counters.
- `Assets/Scripts/System/Common/CombatLifetimeSystem.cs`: shared projectile and
  AOE lifetime expiry.
- `Assets/Scripts/System/Common/DamageDispatchBridge.cs`: native damage queue
  finalize plus managed replay into `ICombatTarget.ReceiveHits`.

## Runtime Ownership

`CombatRoot` owns AOE authoring and bridge work:

- register `AoeConfig` and `AoeTypeDefinition`
- validate AOE type ids and spawn geometry
- build AOE render resources
- append `AoeSpawnEvent` values to the shared scope buffer for managed
  submissions
- expose the target registry for its faction

AOE ECS systems own:

- spawn event expansion
- slot reuse and cold creation
- lifetime expiry
- contact gate maintenance
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

`AoeSpawnEvent` is intent. It can come from:

- `CombatRoot.Spawn(AoeSpawnRequest)`
- projectile impact AOE snapshots
- stack-triggered managed spawns through mob/player hit handling

`AoeSpawnCommand` is one resolved entity allocation request. It contains the
position, bounds, type id, lifetime, repeat cooldown, hit payload, render data,
and optional projectile burst snapshot for one AOE.

Current flow:

1. Managed code appends `AoeSpawnEvent` to the shared scope buffer, or ECS
   producers enqueue events into `AoeSpawnExpansionSystem.EventQueue`.
2. `AoeSpawnExpansionSystem` drains the native event queue and the shared scope
   `DynamicBuffer<AoeSpawnEvent>`.
3. Expansion resolves bounds and writes `AoeSpawnCommand` values to a
   `NativeStream`.
4. `AoeSpawnApplySystem` reads commands, buckets them by faction and type, and
   queries reusable AOE slots with `WithDisabled<Active>()`.
5. Reused slots are reset in an `IJobChunk`.
6. Remaining commands cold-create entities through an `EntityCommandBuffer`.

AOE expansion is currently simple because AOE multiplicity is mostly resolved
before the event reaches ECS. Keep expansion as the place for any future scatter
or pattern math.

## Entity Data And Reuse

AOE entities carry:

- `AoeTag`
- `AoeIdentityComponent`
- generic `Active`
- `CombatLifetimeComponent`
- `CombatKinematicsComponent`
- `CombatCollisionComponent`
- `AoeCollisionActiveTag`
- `AoeHitGateComponent`
- `AoeHitSpawnComponent`
- `AoeAreaComponent`
- `AoeContactGateElement`
- `AoePulseVfxComponent`
- common render components

Runtime despawn disables `Active` and `CombatRenderActiveTag`. Entities remain
available for reuse until the owning `CombatRoot` tears down its faction data.

`AoeCollisionActiveTag` is separate from `Active`. It lets visual-only AOEs stay
active/renderable while collision skips them.

## Damage Timing

Pulse AOE:

- `CombatLifetimeComponent` is disabled for the entity.
- Collision runs once.
- The AOE deactivates after that collision pass.

Lingering AOE:

- `CombatLifetimeComponent` is enabled with remaining lifetime.
- `CombatLifetimeSystem` expires it when remaining time reaches zero.
- Collision can hit immediately.
- Per-target repeat gates prevent repeated hits until their cooldown expires.

There is no global AOE tick. Repeat timing belongs to each AOE-target contact
gate.

## Collision And Consequences

`AoeCollisionSystem` owns hit qualification and pulse source state. It may:

- query target proxy data
- build occupied target cells keyed by `TargetFaction`
- perform bounds and narrow-phase checks
- create per-target contact gates
- disable pulse AOEs after their one collision pass
- emit plain data events for damage, projectile bursts, and VFX

It may not:

- call managed target callbacks
- spawn projectiles directly
- instantiate visual effects
- read `TargetCompanion`

Accepted hits can produce:

- `DamageReplayEvent` into `DamageDispatchBridge.DamageQueue`
- `ProjectileSpawnEvent` into projectile expansion for AOE projectile bursts
- `VfxPendingSpawn` into the shared VFX scope buffer through a flush job

Damage is finalized by `DamageFinalizeSystem` before spawn expansion. Managed
replay runs later in `DamageDispatchBridge` during `PresentationSystemGroup`.

## Projectile Burst From AOE

AOEs can carry an `AoeProjectileBurstSnapshot` in `AoeHitSpawnComponent`.
On an accepted AOE hit, `AoeCollisionSystem` converts that snapshot into a
`ProjectileSpawnEvent` by calling `ProjectileSpawnPipeline.BuildBurstEvent`.
The projectile expansion and apply systems then handle volley expansion, reuse,
and cold creation.

This keeps AOE collision as an event producer, not an entity allocator.

## Lifetime And Pulse VFX

`CombatLifetimeSystem` handles lingering AOE expiry with the same common
`CombatLifetimeComponent` used by projectiles. When lifetime expires, it
disables `Active`, disables `CombatRenderActiveTag`, and emits expire VFX.

`AoePulseVfxSystem` handles interval-based pulse VFX for active lingering AOEs.
It is separate from hit qualification and from lifetime expiry.

## Rendering And VFX

AOE visuals use the same batched rendering path as projectiles:

- `CombatRoot` builds render resources from `AoeConfig` or
  `AoeTypeDefinition`.
- AOE entities carry common render components and faction/type shared
  components.
- `CombatRenderPrepareSystem` writes object matrices.
- `CombatBatchedRenderSystem` submits instances in `PresentationSystemGroup`.

AOE gameplay does not depend on live visual GameObjects.

VFX requests flow as data:

- collision and lifetime produce `VfxPendingSpawn`
- flush jobs append `VfxSpawnRequestElement` to the shared scope
- `CombatVfxDispatchSystem` drains the scope buffer and dispatches through
  `CombatVfxRoot`

## Current Frame Order

Important simulation ordering:

1. `CombatLifetimeSystem` expires projectile and AOE lifetime.
2. `AoePulseVfxSystem` emits periodic pulse VFX for lingering AOEs.
3. Projectile tracking, movement, contact gates, and collision run.
4. `AoeContactGateSystem` expires AOE contact gates.
5. `AoeCollisionSystem` emits damage, projectile spawn, and VFX events.
6. `DamageFinalizeSystem` freezes the native damage queue.
7. `ProjectileSpawnExpansionSystem` and `AoeSpawnExpansionSystem` drain events
   and produce commands.
8. `AoeSpawnApplySystem` and projectile apply systems reuse slots and
   cold-create overflow.
9. `CombatRenderPrepareSystem` prepares render matrices.
10. Presentation systems dispatch damage, VFX, and render batches.

AOEs spawned/reused by apply systems do not collide until the next simulation
update because apply runs after collision.

## Authoring Notes

Current AOE authoring uses:

- `AoeConfig` assets
- `AoeTypeDefinition`
- `BasicAoePrefab`, `LingeringAoePrefab`, or other validator prefabs
- `AoeSpawnGeometry` resolved before ECS receives the spawn event
- `AoeSpawnRequest` for managed spawn submission

AOE template prefabs provide visual and collider authoring data, but runtime
AOE gameplay uses ECS components. Do not add live trigger damage behavior to AOE
prefabs as the authoritative path.

`CombatRoot.RegisterConfig` and `CombatRoot.RegisterType` assign runtime type
ids and build render resources. `CombatRoot.Spawn(AoeSpawnRequest)` validates
the type id and resolved geometry before appending an `AoeSpawnEvent`.

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
- native queues/streams for damage, spawn, and VFX events
- batched render submission

Revisit only with profiling:

- AOE spatial hash cell size
- single-bucket spawn reuse parallelism
- per-hit damage replay volume
- pulse VFX density and budgets

## Known Gaps

- Damage replay is still one event per qualifying hit before
  `DamageDispatchBridge` groups by target for callback dispatch.
- Crit rolling currently happens in the managed bridge.
- Dedicated AOE stress scenes and hard pass/fail thresholds are still limited.
- AOE expansion is intentionally minimal today; future scatter/pattern work
  should live in `AoeSpawnExpansionSystem`.

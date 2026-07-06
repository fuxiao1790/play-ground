# Architecture

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

## Runtime Shape

`play-ground` uses a hybrid scene-object plus data-runtime architecture.

Low-count authored gameplay stays on Unity scene objects:

- player
- mobs
- walls/environment
- camera
- spawner roots and spawn points
- debug overlay
- audio manager

High-count combat gameplay uses Entities/DOTS:

- projectiles
- AOEs
- future beams/lasers
- high-count transient hit and VFX requests
- target proxy data used by combat collision

Player and mobs use Rigidbody2D, Collider2D, Animator, SpriteRenderer, and
normal prefab composition. Projectiles and AOEs are not represented by one
GameObject per gameplay entity.

The project intentionally mixes OOP and DOP. MonoBehaviours own authored
references, Unity object lifetimes, and gameplay callbacks. ECS systems own
scalable combat state, simulation, pooling, and event buffers. The boundary must
stay narrow and explicit.

## High-Level Runtime Path

1. `Assets/Scenes/Main.unity` loads gameplay roots.
2. `GameRoot` binds scene-level services and registries.
3. `PlayerRoot` samples movement, dash, facing, animation, health, and skill
   loadout helpers.
4. `MobSpawnerRoot` asks spawn points for spawn requests and enforces caps.
5. `MobRoot` updates behavior through triggers, local event queues, and behavior
   selection.
6. Each `CombatRoot` represents one firing faction. The player root and mob root
   share one ref-counted ECS world and one ref-counted `CombatScope` entity.
7. Actor roots register `ICombatTarget` instances with combat target registries.
   The registries create ECS target proxy entities.
8. Player and mob roots push proxy position and shape in `Update()` and delete
   dead proxies in `LateUpdate()`.
9. Managed attacks submit `ProjectileSpawnRequest` or `AoeSpawnRequest` to
   `CombatRoot`. The root converts them into `ProjectileSpawnEvent` or
   `AOE variant spawn event` on the shared scope buffer.
10. ECS producers can also enqueue typed spawn events directly into expansion
    systems.
11. Expansion systems convert spawn events into one-entity spawn commands.
12. Apply systems reuse disabled `Active` entities and cold-create only overflow
    commands.
13. Simulation systems move, track, expire, collide, and emit typed consequence
    events.
14. `DamageFinalizeSystem` freezes native damage events.
15. Presentation systems dispatch damage, VFX, and render batches.

## Ownership Map

- `GameRoot`: scene-level service binding and diagnostics.
- `PlayerRoot`: player movement, facing, skill driver, health, target proxy
  push/delete, and `ICombatTarget` implementation.
- `MobRoot`: mob health, behavior, projectile attack, status stacks, death
  cleanup, target proxy push/delete, and `ICombatTarget` implementation.
- `MobSpawnerRoot`: global spawn cap and mob instantiation.
- `SpawnPoint`: local spawn timing, overlap checks, and optional spawn pool.
- `CombatRoot`: per-faction combat bridge. Owns target registry, projectile/AOE
  type registration, render resources, managed spawn submission, and faction
  registration. Shares ECS world and scope through ref-counted owners.
- `CombatEcsWorld`: creates or acquires the default ECS world and releases an
  owned world on final combat root teardown.
- `CombatScopeOwner`: creates one shared `CombatScope` entity with projectile,
  AOE, and VFX buffers; destroys it after the last combat root releases it.
- `CombatTargetRegistry`: records managed targets and creates target proxy
  entities when the root's proxy binding is ready.
- `CombatTargetProxy`: creates, pushes, and deletes ECS proxy entities for
  `ICombatTarget` objects.
- `DamageDispatchBridge`: the only approved reader of `TargetCompanion`; groups
  finalized `DamageReplayEvent` values by proxy and calls
  `ICombatTarget.ReceiveHits`.
- `CombatLifetimeSystem`: shared projectile and AOE lifetime expiry over
  `CombatLifetimeComponent` and generic `Active`.
- `CombatRenderPrepareSystem`: prepares matrices for active projectile and AOE
  renderables.
- `CombatBatchedRenderSystem`: submits instanced sprite batches in
  `PresentationSystemGroup`.
- `CombatVfxRoot` and `CombatVfxDispatchSystem`: own VFX resources and dispatch
  buffered VFX requests.

## Projectile Ownership

- `ProjectileSpawnPipeline`: defines `ProjectileSpawnEvent` and
  `ProjectileSpawnCommand`.
- `ProjectileSpawnExpansionSystem`: drains event queues and scope buffers,
  resolves volley math, and writes the projectile command queue.
- `ProjectileSpawnApplySystem`: reuses or creates projectile slots and toggles
  timed spawn through enabled `TimedSpawnComponent`.
- `TimedSpawnSystem`: emits child projectile or AOE events from active timed
  spawn sources.
- `ProjectileTrackingSystem`: target proxy acquisition and homing steering.
- `ProjectileMovementSystem`: position integration and bounds refresh.
- `ProjectileContactGateSystem`: repeat-hit gate expiry.
- `ProjectileCollisionSystem`: target proxy broad phase, narrow-phase hit
  checks, contact gates, pierce, source deactivation, and damage/spawn/VFX event
  output.

## AOE Ownership

- `AoeSpawnPipeline`: defines `AOE variant spawn event` and `AoeSpawnCommand`.
- `AOE spawn expansion systems`: drains event queues and scope buffers, resolves
  bounds, and writes impact or lingering command queues.
- `ImpactAoeSpawnApplySystem`: reuses or creates lean impact AOE slots.
- `LingeringAoeSpawnApplySystem`: reuses or creates lingering AOE slots and
  toggles timed spawn through enabled `TimedSpawnComponent`.
- `AoePulseVfxSystem`: interval pulse VFX for lingering AOEs.
- `ImpactAoeCollisionSystem`: target proxy broad phase, narrow-phase hit checks,
  impact deactivation, and damage/spawn/VFX event output.
- `LingeringAoeCollisionSystem`: lingering tick interval countdown, target proxy
  collision, and damage/spawn/VFX event output.

## ECS Boundary Rules

Projectile and AOE entities must carry explicit domain tags:

- `ProjectileTag`
- `AoeTag`

The generic `Active` component is only an occupancy flag. It never opts an
entity into a domain system by itself.

There is one shared `CombatScope` entity for combat. Scope membership does not
mean domain or faction. Domain comes from domain tags. Faction comes from
`CombatFaction`.

Simulation jobs may read:

- unmanaged ECS components
- target proxy position, shape, and faction data
- native queues, streams, and arrays

Simulation jobs must not read:

- GameObjects
- Transforms
- Colliders
- Physics2D
- ScriptableObjects as live runtime state
- managed `TargetCompanion`

Only `DamageDispatchBridge` may resolve `TargetCompanion` to call managed target
callbacks.

## Spawn Pipeline Rule

Spawn intent and allocation intent are separate.

Events are gameplay intent:

- `ProjectileSpawnEvent` can contain count, spread, jitter, base direction,
  speed, child-spawner data, hit payload, tracking, and render data.
- `AOE variant spawn event` contains one AOE intent today, and future AOE scatter or
  pattern math should still be handled by expansion.

Commands are one entity:

- `ProjectileSpawnCommand` describes exactly one projectile entity.
- `AoeSpawnCommand` describes exactly one AOE entity.

Expansion owns spawn math. Apply owns entity reuse and cold creation. Collision
may emit final typed consequence events, but it may not allocate entities or
call managed targets.

## Damage And Consequence Flow

Collision systems emit plain data:

- `DamageReplayEvent` into `DamageDispatchBridge.DamageQueue`
- `ProjectileSpawnEvent` for impact projectiles or AOE projectile bursts
- `AOE variant spawn event` for projectile impact AOEs
- `VfxPendingSpawn` for hit and expire VFX

`DamageFinalizeSystem` runs after projectile and AOE collision and before spawn
expansion. It completes producers, drains the native damage queue into a frozen
array, and clears the queue.

`DamageDispatchBridge` runs in `PresentationSystemGroup`. It sorts events by
target proxy, rolls crits on the main thread, builds `CombatHitData`, resolves
the managed companion, and calls `ReceiveHits`.

Internal follow-up spawns never cross into managed target callbacks. They stay
as typed spawn events and flow through expansion and apply.

## Target Proxy Lifecycle

The target bridge is proxy data plus managed replay:

1. Actor registers with a combat root's target registry.
2. Registry creates an ECS proxy with `TargetProxyTag`, `TargetPosition`,
   `TargetCollisionShape`, `TargetFaction`, and `TargetCompanion`.
3. Actor stores the proxy `Entity`.
4. Actor pushes proxy position and shape in `Update()`.
5. Collision and tracking systems read unmanaged proxy data.
6. Damage events reference the proxy `Entity`.
7. `DamageDispatchBridge` resolves the companion during presentation replay.
8. Dead or disabled actors queue proxy deletion and delete in `LateUpdate()`.

This lifecycle avoids using live Unity colliders in high-count collision loops
while keeping final health/status mutation on the actor roots.

## Performance Architecture

Combat must support extreme scaling.

Core rules:

- pool anything that can spawn repeatedly
- prefer `Active` enable/disable over destroy/create churn
- bake prefab-derived collision/render data before hot loops
- keep GameObject, Transform, Collider2D, Animator, ParticleSystem, and
  AudioSource access out of inner simulation loops
- use explicit target registries and target proxies instead of scene scans
- snapshot gameplay data before spawn
- replay gameplay callbacks after simulation stages
- use batched rendering for projectile and AOE visuals
- expose counters and profiler markers before stress cases are hard to explain

Projectile performance target remains about 50k projectiles on screen with 20
targets at 120 fps. Treat that as a design pressure, not a hard guarantee for
every intermediate implementation.

Important counters:

- active projectiles
- projectile simulation milliseconds
- projectile render milliseconds
- active AOEs
- AOE simulation milliseconds
- active beams
- active mobs
- active particle/VFX workload
- spawned/despawned or reused objects per second
- managed allocations per frame

## Player System

`PlayerRoot` coordinates:

- `PlayerMovement`
- `PlayerFacing`
- `PlayerSkillDriver`
- `PlayerHealth`
- `PlayerStateDriver`
- `PlayerAnimatorDriver`
- `StatusEffects`
- target proxy lifecycle

One held fire input can perform every ready equipped skill in the same frame.

## Mob System

`MobRoot` coordinates:

- Rigidbody2D and colliders
- health and soft death
- behavior triggers and state driver
- optional projectile attack
- stack/status handling
- target proxy lifecycle
- managed hit replay through `ICombatTarget`

Stack-triggered AOEs are decided on the mob/status side and submitted through a
combat root. The AOE runtime only materializes and resolves the spawned AOE.

## Camera And Play Area

`GameplayCamera` should provide:

- orthographic follow
- mouse-biased framing
- dead-zone or boundary around the player
- mouse wheel zoom
- authored camera limits

`PlayAreaRoot` should own:

- configurable arena bounds
- wall visuals or authored wall prefab references
- environment colliders
- layer assignment

Player and mobs should collide with walls through Physics2D, not custom wall
logic.

## Future Work

- beam/laser runtime for continuous or sweeping attacks
- stronger damage aggregation before managed replay
- explicit VFX and particle budgets
- audio manager with pooled one-shots and duplicate culling
- dedicated stress scenes and thresholds for projectile, AOE, VFX, and render
  cost

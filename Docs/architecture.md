# Architecture

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Runtime Shape

- small root components coordinate
- focused helpers and ScriptableObjects decide behavior
- data-oriented runtime cores handle high-volume projectile and AOE simulation
- scene and prefab composition owns authored setup
- attack scaling and visual density are planned gameplay pillars, so performance
  budgets shape system design from the first implementation

This is a hybrid scene-object plus data-runtime architecture. Player and mobs
are normal Unity scene objects because their count is low and built-in Physics2D
is good for body movement and wall collision. Projectiles, AOEs, and future
beams live in data-oriented runtime worlds because their counts can become huge.
Projectiles specifically use Entities/DOTS so high-count projectile state is not
represented by one GameObject or scene node per projectile.

High-level runtime path:

1. `Assets/Scenes/Main.unity` loads gameplay roots.
2. `GameRoot` binds scene-level services and registries.
3. `PlayerRoot` samples movement, dash, facing, animation, and attack loadout helpers.
4. `MobSpawnerRoot` asks spawn points for spawn requests and enforces caps.
5. `MobRoot` drains local events and updates behavior through a separate behavior FSM.
6. Projectile roots enqueue spawn requests and sync target snapshots into ECS
   scope entities, projectile ECS systems materialize/reuse projectile entities
   and simulate hits, and roots replay hit events.
7. `DebugOverlay` gathers scene-level counters.

## Scene And Prefab Ownership

General rule:

- each gameplay object has one root GameObject with one root MonoBehaviour
- the root component owns serialized references, setup validation, event wiring, subsystem construction, and update order
- child components, plain C# classes, or ScriptableObjects own focused gameplay decisions
- prefabs own authored composition such as colliders, sprites, attack children, behavior configs, and visual templates
- runtime systems should depend on typed references and explicit registries, not hidden scene discovery

## Target Ownership Map

- `Assets/Scenes/Main.unity`: entry composition root with camera, player, play area, spawner, projectile roots, AOE roots, and debug overlay
- `GameRoot`: binds shared scene services, target registries, and high-level diagnostics
- `PlayAreaRoot`: owns configurable arena bounds, wall visuals, and environment colliders
- `PlayerRoot`: owns Rigidbody2D, body collider, hurtbox, sprite/Animator, attack child components, movement helper, facing helper, animation helper, and attack loadout
- `BasicAttackPrefab`: authored attack piece with a `Visual` child
  `SpriteRenderer` and a `Hurtbox` child `CircleCollider2D`, `BoxCollider2D`,
  or `CapsuleCollider2D`
- `ProjectileAttack`: container attack component/prefab that references one primary basic attack prefab, optional child basic attack prefab, and owns recovery, damage, sound, tracking, count, spread, jitter, pierce, and optional impact AOE
- `AoeAttack`: attack component/prefab that owns AOE effect template, damage, lifetime, tick interval, count, burst radius, and spawn position mode
- `MobRoot`: owns Rigidbody2D, body collider, hurtbox, sprite/Animator, health, behavior FSM, local event queue, trigger updates, selected behavior, optional projectile attack, and soft death
- `MobSpawnerRoot`: owns global spawn cap and final mob instantiation
- `SpawnPoint`: owns local timer, overlap checks, and optional spawn pool
- `ProjectileRoot`: owns one scoped projectile flow, target registry reference, template baking, listener maps, event replay, and rendering coordination
- `ProjectileSimulationSystem`: clears per-scope projectile event buffers at the start of the simulation stage
- `ProjectileSpawnSystem`: drains scoped projectile spawn request buffers, reuses
  inactive projectile entities, and creates cold entities through ECB only when
  no reusable entity exists
- `ProjectileTrackingSystem`: owns homing target refresh, reacquire cadence, and steering
- `ProjectileMovementSystem`: owns projectile position integration
- `ProjectileChildSpawnSystem`: owns timed child projectile spawn request events
- `ProjectileLifetimeSystem`: owns lifetime countdown and disabling expired projectile entities
- `ProjectileContactGateSystem`: owns repeat-hit gate cooldown expiry
- `ProjectileCollisionSystem`: owns projectile target mask filtering, baked-shape hit checks, pierce handling, and ordered hit event output
- `ProjectileRoot`: owns scoped projectile bridge cleanup and destroys scoped entities only when the root tears down
- `ProjectileCollisionMath`: owns pure circle, rectangle, and capsule narrow-phase math
- `AoeRoot`: owns one scoped AOE target flow, AOE template baking, target sync, optional effect lifetime, hit replay, and spawn requests
- `AoeWorld`: owns only plain runtime AOE data, target queries, pulse hits, lingering ticks, and re-entry gates
- `BeamRoot`: future scoped beam/laser flow for continuous or sweeping attacks,
  target snapshots, tick gates, and visual line/batch ownership
- `AudioManager`: owns one-shot audio pooling and duplicate culling
- `DebugOverlay`: owns shared scene-level text

## Hybrid Runtime Boundary

Scene-object side:

- player
- mobs
- walls/environment
- camera
- spawner roots and spawn points
- debug overlay
- audio manager

Use Unity built-ins here:

- Rigidbody2D movement for player and mobs
- Collider2D contacts between player, mobs, and walls
- normal Transform hierarchy for authored prefabs
- Animator/SpriteRenderer for low-count actors

Data-runtime side:

- projectiles
- AOEs
- beams/lasers
- high-count transient hit effects
- target snapshots used by attack collision

Do not move player and mob body collision into the projectile/AOE runtime. Also
do not move high-count projectiles and AOEs into one GameObject per gameplay
entity as the authoritative simulation path.

The bridge is snapshots and callbacks:

1. actor GameObjects register hurtboxes with target registries
2. attack roots snapshot target positions and baked hurtbox shapes
3. data runtimes or ECS systems simulate hits
4. roots replay hit events back to actor components
5. actors apply health, status stacks, animation requests, and soft death

## Performance Architecture

Combat must support extreme scaling.

Core rules:

- pool anything that can spawn repeatedly
- bake prefab-derived collision/render data once
- keep hot-loop simulation out of MonoBehaviour methods where practical
- keep GameObject, Transform, Collider2D, Animator, ParticleSystem, and AudioSource
  access out of inner simulation loops
- use explicit target registries instead of scene scans
- snapshot gameplay data before spawn
- replay gameplay callbacks after simulation stages
- prove batched projectile visuals early; use pooled visuals only where they fit
  the scale target
- expose counters so stress cases can be measured early

Initial stress scenes should expose counters before fixed pass/fail thresholds
exist. Projectile performance target: about 50k projectiles on screen with 20 targets at 120 fps.
Important counters:

- active projectiles
- projectile simulation milliseconds
- projectile render milliseconds
- active AOEs
- AOE simulation milliseconds
- active beams
- beam query milliseconds
- active mobs
- active particle systems
- spawned/despawned objects per second
- managed allocations per frame

## Player System

`PlayerRoot` should coordinate:

- `PlayerMovement`: input vector, acceleration, friction, dash, Rigidbody2D velocity
- `PlayerFacing`: sprite flip or aim rotation toward mouse
- `PlayerAttackLoadout`: child attack discovery, max attack count, held-fire updates
- `PlayerStateDriver`: locomotion state decisions
- `PlayerAnimatorDriver`: animation requests and priority rules
- `StateMachineCore`: reusable transition callback core

Attack children under the player's `Attacks` child are equipped attacks. Each
attack container child owns its Transform offset. One held fire input can
perform every ready equipped attack in the same frame.

## Camera System

`GameplayCamera` should provide:

- orthographic follow
- mouse-biased framing
- oval dead-zone or boundary around the player
- mouse wheel zoom
- authored camera limits for normal play
- optional expanded debug limits for local inspection

## Play Area

The prototype arena currently uses 1280x720 world units for convenience, but the
arena size must be serialized/configurable.

`PlayAreaRoot` should own:

- bounds
- wall visual generation or authored wall prefab references
- environment colliders
- layer assignment

Player and mobs should collide with walls through Physics2D, not custom wall logic.

## Projectile And AOE Systems

Keep projectile and AOE roots scoped by target set:

- player projectiles target mobs
- mob projectiles target player
- player AOEs target mobs
- mob AOEs target player

The data runtime should not touch live Unity objects. It receives snapshots and returns events. The root adapts those events back into gameplay callbacks.

Lasers and beams are future work. When added, they should get their own scoped
runtime instead of being modeled as projectile spam.

## Audio System

`AudioManager` should own pooled one-shot playback.

Gameplay systems request sound playback with:

- clip
- world position
- priority placeholder
- same-clip simultaneous cap

Do not let attacks create unbounded AudioSource objects.

## Implementation Order

Recommended first code port:

1. folder structure and shared coding patterns
2. input actions and player movement
3. camera and play area
4. debug overlay
5. spawn root and one mob prefab
6. mob behavior FSM
7. projectile runtime
8. AOE runtime
9. audio manager
10. focused tests and stress scenes
11. beam/laser runtime skeleton when beam gameplay becomes active work

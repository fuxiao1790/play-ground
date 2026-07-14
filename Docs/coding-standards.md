# Coding Standards

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be checked against code before implementation work.

## Root Component Rule

Each gameplay object prefab should have one root MonoBehaviour that coordinates
that object.

Examples:

- player
- mob
- attack or skill driver
- combat root
- spawner
- play area
- VFX root

The root component owns Unity references, setup validation, event wiring, update
order, and the public API. It should not own all gameplay decisions directly.

## Root Component Should Do

- keep serialized references to required child objects and config assets
- strongly prefer dependency injection through the Unity Inspector over resolving dependencies in lifecycle methods
- validate required references in `Awake()` or `OnValidate()`
- construct or bind focused helper classes
- wire events between subsystems
- define update order in `Update()` or `FixedUpdate()`
- expose clear methods other systems call

## Root Component Should Not Do

- contain all movement math directly
- contain all combat rules directly
- choose animation state directly from raw gameplay data
- perform high-volume collision loops mixed with scene-object code
- search the scene repeatedly during gameplay

Gameplay logic belongs in focused classes, ScriptableObjects, child components,
or data-oriented runtime cores.

## Awake vs OnEnable Boundary

`Awake()` may only set up state that belongs to the component itself: resolve
self-owned references, validate fields, construct helper objects, and configure
children.

Cross-MonoBehaviour work belongs in `OnEnable()` or `Start()` unless the
dependency is explicitly constructed and owned by the current component. This
includes registering with roots, subscribing to events, and calling into other
scene components.

Unity guarantees all `Awake()` calls for scene-loaded objects complete before
any `OnEnable()` fires, so `OnEnable()` is the safer boundary for touching other
MonoBehaviours.

## Fail Fast Validation

Serialized fields that are required must be validated once at setup.

Preferred:

- required field is assigned in the inspector
- `Awake()` validates and throws a clear setup error if missing
- gameplay methods assume dependencies are valid

Avoid:

- repeated null checks in hot paths
- allowing half-configured prefabs to keep running
- disabling required gameplay components to hide bad setup
- hiding bad setup with no-op behavior

Use `OnValidate()` for editor feedback, but keep real runtime validation in
setup too.

## Unity Object Access

Avoid repeated `FindObjectOfType`, `GameObject.Find`, tag scans, or broad scene
searches on hot paths.

Preferred:

- serialized references
- scene root binding
- explicit registries
- target proxy entities for combat collision
- cached component references

Reflection is only for editor tooling, tests, or diagnostics. Do not use
reflection or string-built method lookup for gameplay decisions.
Do not use `unsafe` code in gameplay or shared runtime systems.

## Update Timing

Use `FixedUpdate()` for Rigidbody2D movement and physics-backed actor behavior.

Use `Update()` for:

- input sampling
- cooldowns that do not need fixed-step precision
- camera follow smoothing unless physics coupling demands otherwise
- target proxy push from player and mob roots
- debug text updates

Use `LateUpdate()` for queued target proxy deletion on actors. This keeps proxy
entities valid through the simulation and presentation work that may still
reference them in the current frame.

Do not hide expensive setup inside repeated runtime calls. Scene/prefab baking,
collider shape extraction, material setup, and pool creation should happen
during setup, registration, or preload.

## Hybrid ECS/Scene Rule

Use scene objects for low-count actors and environment:

- player
- mobs, with player plus mobs below roughly `50` as an early performance target
- walls and level collision
- camera and debug UI

Use data-oriented runtime worlds for high-count combat entities:

- projectiles
- AOEs
- beams/lasers
- transient chained hit effects
- combat VFX request streams

Shared combat ECS components under `Assets/Scripts/System/Combat/` must stay
owned by the narrowest combat concept that explains why they exist. Systems that
consume cross-feature components must also require a domain tag such as
`ProjectileTag` or `AoeTag`. There is one shared `CombatScope` entity for
combat, so scope membership and reusable lifecycle components alone never imply
domain or faction. Domain comes from domain tags. Faction comes from
`CombatFaction`.

Player and mob movement/collision should use Unity Physics2D. Projectile and AOE
hit simulation should use target proxy entities and baked shapes instead of
thousands of live trigger objects.

Managed target references are restricted. `TargetCompanion` may exist on proxy
entities, but only `CombatApplyBridge` may read it to call
`ICombatTarget.ReceiveCombatTick`.

## Combat Event Separation

Damage application must stay separate from internal ECS spawn and VFX payloads.
Keep these as distinct typed paths:

- `ProjectileSpawnEvent` and `AOE variant spawn event` carry spawn follow-up intent into
  expansion systems.
- `ProjectileSpawnCommand` and `AoeSpawnCommand` carry one-entity allocation
  intent into apply systems.
- `CombatHitEvent` carries raw hit data into ECS finalization.
- `CombatTickResult` carries aggregate damage/status presentation data through
  `CombatApplyBridge`.
- `AoeVfxSpawnRequest` and `AoeVfxSpawnRequestElement` carry visual-only requests into
  VFX dispatch.

Do not widen damage events with spawn-routing fields. Do not widen spawn events
with target-replay-only data. Do not route internal spawn follow-ups through
managed target callbacks.

## System Encapsulation

ECS systems must not reach into other systems' private fields or collections.

Avoid:
- `GetExistingSystemManaged<T>()` followed by direct field access (`.EventQueue`, `.HitQueue`, etc.)
- Systems calling `AsParallelWriter()` on another system's owned collections
- Cross-system field inspection for state checks (e.g., `otherSystem.Queue.IsCreated`)

This pattern creates invisible coupling that balloons as systems proliferate. Each new
event producer must be wired into every event consumer, turning the system graph into
a brittle mesh.

Preferred:
- Use a **singleton component** that owns shared data structures (native containers, queues, etc.)
- All systems access the singleton via `SystemAPI.GetSingleton<T>()` or
  `SystemAPI.TryGetSingletonRW<T>()` on the main thread
- For native containers in singletons, extract them, schedule jobs against the container
  itself (not the component), and flow dependencies through `state.Dependency`

Example pattern (see `CombatStatsSingleton`):
- `CombatStatsSingleton` holds accumulated counters and cross-system data
- Spawn systems, collision systems, and VFX systems all write to it:
  ```csharp
  if (SystemAPI.TryGetSingletonRW<CombatStatsSingleton>(out RefRW<CombatStatsSingleton> stats))
  {
      stats.ValueRW.EntitiesSpawned += count;
  }
  ```
- Stats display system reads the singleton and presents data

For event queues and native containers:
- Store `NativeQueue<T>` or similar in the singleton (e.g., `TargetSpatialHashSingleton`)
- Extract on main thread: `var singleton = SystemAPI.GetSingleton<T>()`
- Schedule jobs against `singleton.Queue`, not the component
- Unity's singleton functions are designed for this to avoid sync points while chaining jobs

Canonical combat-lane examples:
- `CombatAoeVfxDispatchSingleton`
- `CombatHitDispatchSingleton`
- `ProjectileSpawnEventSingleton`
- `ImpactAoeSpawnEventSingleton`
- `LingeringAoeSpawnEventSingleton`

These hold the lane's native queue/list plus explicit `JobHandle` fields. Producers use
`SystemAPI.TryGetSingletonRW<T>()` and combine their scheduled job handle into the
singleton on the main thread; sinks complete the stored handles before draining and dispose
the native containers during their own teardown.

Benefits:
- No `GetExistingSystemManaged` reaching into fields; dependencies are compiler-visible
- Job dependency chains work correctly through native container ownership
- New producers don't require rewiring every consumer
- Encapsulation enforced by the ECS type system

## ECS Lifecycle Comments

ECS component, tag, buffer, and shared-component declarations that document
their entity lifecycle must be updated in the same change that alters that
lifecycle.

Use the searchable prefix `ECS Lifecycle:` for these comments.

Examples of lifecycle changes:

- component is added or removed at a new runtime point
- component changes from structural add/remove to enable/disable
- component becomes optional or becomes part of the base archetype
- buffer ownership, clearing, reuse, or teardown behavior changes
- shared component value starts changing after creation

## Allocation Rule

Combat paths should be allocation-light.

Hot paths include:

- projectile spawn expansion and apply
- projectile simulation
- AOE spawn expansion and apply
- AOE simulation
- mob spawn
- mob AI update
- attack perform
- damage dispatch
- render batch update
- VFX dispatch

Use reusable lists, arrays, pools, native containers, or Entities/DOTS where the
system needs them. For high-volume projectile and AOE work, use Unity Entities
plus Jobs/Burst.

Treat repeated allocations, Instantiate/Destroy churn, per-frame LINQ, closure
captures, broad component lookups, and implicit array copies in combat code as
bugs unless measurement proves they are harmless.

## Native And ECS Handle Ownership

Any code that creates a native, ECS, or GPU handle owns cleanup unless it
transfers ownership explicitly.

Examples:

- `EntityQuery` created by `EntityManager.CreateEntityQuery(...)` must be
  disposed by the owner.
- `NativeArray`, `NativeList`, `NativeQueue`, `NativeStream`,
  `GraphicsBuffer`, and similar native/GPU resources must be disposed or
  released on every teardown path.
- A custom `World` added to the player loop must be removed from the player loop
  and disposed by the same ownership layer that created it.

Avoid:

- creating ECS queries repeatedly without matching `Dispose()`
- creating a world from a root component without reference-counted ownership
- relying on Unity playmode teardown to clean persistent native allocations
- hiding cleanup behind a flag when a partial setup path may already have
  allocated native handles

## Performance Budget Rule

Every scalable combat system should have an obvious budget and fallback path.

Examples:

- projectile and AOE visuals use batched sprites for high counts
- particles and VFX can cap emissions or skip low-priority effects
- impact sounds can be culled by same-clip and priority rules
- damage numbers and decals can be pooled and capped
- beams can tick damage at authored intervals rather than every rendered frame

Debug counters should exist before content stress tests become hard to explain.

## ScriptableObject Rule

Use ScriptableObjects for reusable authored data:

- skill definitions
- support definitions
- mob behavior config
- mob trigger config
- spawn pools
- AOE and projectile definitions
- damage/status references

ScriptableObjects should not store per-instance mutable combat state unless
they are intentionally runtime-created clones. Per-mob health, cooldowns,
selected behavior, target references, and runtime skill slot state belong on
runtime objects.

## Debug UI Ownership

Shared top-left debug text belongs to `DebugOverlay`.

Gameplay objects may expose diagnostic data, but they should not write directly
to the shared label.

Per-object debug widgets can live under the object that owns the data.

## Test Hooks

Do not add production-only metadata, flags, counters, or breadcrumbs just for
tests.

Tests should observe through:

- public runtime APIs
- real gameplay effects
- test-owned listener components
- explicit diagnostic APIs that are useful outside tests

Test-only prefabs and scenes belong under `Assets/Tests/`.

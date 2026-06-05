# Coding Standards

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Root Component Rule

Each gameplay object prefab should have one root MonoBehaviour that coordinates that object.

Examples:

- player
- mob
- attack
- projectile root
- AOE root
- spawner
- play area

The root component owns Unity references, setup validation, event wiring, update order, and the public API. It should not own all gameplay decisions directly.

## Root Component Should Do

- keep serialized references to required child objects and config assets
- validate required references in `Awake()` or `OnValidate()`
- construct or bind focused helper classes
- wire events between subsystems
- define update order in `Update()` or `FixedUpdate()`
- expose clear methods other systems call

## Root Component Should Not Do

- contain movement math directly
- contain combat rules directly
- choose animation state directly from raw gameplay data
- perform high-volume collision loops mixed with scene-object code
- search the scene repeatedly during gameplay

Gameplay logic belongs in focused classes, ScriptableObjects, child components, or data-oriented runtime cores.

## Awake vs OnEnable Boundary

`Awake()` may only set up state that belongs to the component itself: resolve
self-owned references, validate fields, construct helper objects, configure
children. It must not call methods on other MonoBehaviours, because Unity does
not guarantee that other `Awake()` calls have completed first.

Cross-MonoBehaviour work — registering types with a root, subscribing to events,
wiring up systems — belongs in `OnEnable()` or `Start()`. Unity guarantees all
`Awake()` calls in a scene complete before any `OnEnable()` fires for those
scene-loaded objects, so by `OnEnable()` every dependency is safe to touch.

Violating this rule produces NullReferenceExceptions that look like logic errors
but are actually execution-order problems. The symptom is a null field that is
clearly initialized in the dependency's own `Awake()`.

## Fail Fast Validation

Serialized fields that are required must be validated once at setup.

Preferred:

- required field is assigned in inspector
- `Awake()` validates and throws a clear setup error if missing
- gameplay methods assume dependencies are valid

Avoid:

- repeated null checks in `Update()`, `FixedUpdate()`, attack fire, spawn, or hit loops
- allowing half-configured prefabs to keep running
- disabling required gameplay components as a way to hide bad setup
- hiding bad setup with no-op behavior

Use `OnValidate()` for editor feedback, but keep real runtime validation in setup too.

## Unity Object Access

Avoid repeated `FindObjectOfType`, `GameObject.Find`, tag scans, or broad scene searches on hot paths.

Preferred:

- serialized references
- scene root binds services once
- explicit registries for target groups
- layer masks for broad Unity filtering
- cached component references

Reflection is only for editor tooling, tests, or diagnostics. Do not use reflection or string-built method lookup for gameplay decisions.

## Update Timing

Use `FixedUpdate()` for Rigidbody2D movement and physics-backed simulation.

Use `Update()` for:

- input sampling
- cooldowns that do not need fixed-step precision
- camera follow smoothing unless physics coupling demands otherwise
- debug text updates

Do not hide expensive setup inside repeated runtime calls. Scene/prefab baking, collider shape extraction, material setup, and pool creation should happen during setup, registration, or preload.

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

Shared combat ECS components under `Assets/Scripts/System/Common/` must stay
domain-neutral. Systems that consume them must also require a domain tag or
scope component such as `ProjectileTag` or `ProjectileScope`; common components
alone should never opt an entity into projectile or AOE behavior.

Player and mob movement/collision should use Unity Physics2D. Projectile and AOE
hit simulation should use target snapshots and baked shapes instead of thousands
of live trigger objects.

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

- projectile spawn
- projectile simulation
- AOE simulation
- mob spawn
- mob AI update
- attack perform
- render batch update

Use reusable lists, arrays, pools, Native containers, or Entities/DOTS where the
system needs them. For high-volume projectile work, use Unity Entities plus
Jobs/Burst.

The intended game has extreme spell scaling. Treat repeated allocations,
Instantiate/Destroy churn, per-frame LINQ, closure captures, broad component
lookups, and implicit array copies in combat code as bugs unless there is a
measured reason they are harmless.

## Native And ECS Handle Ownership

Any code that creates a native or ECS handle owns its cleanup unless it transfers
ownership explicitly.

Examples:

- `EntityQuery` created by `EntityManager.CreateEntityQuery(...)` must be
  disposed by the owner, usually in `OnDestroy()` for MonoBehaviour-owned
  queries.
- `NativeArray`, `NativeList`, `NativeQueue`, `GraphicsBuffer`, and similar
  native/GPU resources must be disposed or released on every teardown path.
- A custom `World` added to the player loop must be removed from the player loop
  and disposed by the same ownership layer that created it.

Bug example:

- `ProjectileRoot` and `AoeRoot` created scope entities, `EntityQuery` handles,
  persistent submit buffers, and sometimes the shared ECS world in `BindWorld()`.
  Teardown destroyed scoped entities and buffers, but did not dispose the query
  handles or the custom world. Unity then reported thousands of persistent leaks
  from `ProjectileRoot.BindWorld()`, `AoeRoot.BindWorld()`, and
  `SubmitQuery<T>()`. The fix was to centralize combat-world ownership and make
  root teardown dispose queries, dispose native buffers, destroy scoped entities,
  and release the world.

Avoid:

- creating ECS queries repeatedly without a matching `Dispose()`
- creating a world from a root component without reference-counted ownership
- relying on Unity playmode teardown to clean persistent native allocations
- hiding cleanup behind `if (runtimeReady)` when a partial setup path may still
  have allocated native handles

## Performance Budget Rule

Every scalable combat system should have an obvious budget and fallback path.

Examples:

- projectile visuals can degrade from individual pooled prefabs to batched sprites
- particles can cap emissions or skip low-priority effects
- impact sounds can be culled by same-clip and priority rules
- damage numbers and decals can be pooled and capped
- beams can tick damage at authored intervals rather than every rendered frame

Debug counters should exist before content stress tests become hard to explain.

## ScriptableObject Rule

Use ScriptableObjects for reusable authored data:

- mob behavior config
- mob trigger config
- spawn pools
- attack definitions when reused across prefabs
- damage type references

ScriptableObjects should not store per-instance mutable combat state unless they are intentionally runtime-created clones. Per-mob health, cooldowns, selected behavior, and target references belong on runtime state objects.

## Debug UI Ownership

Shared top-left debug text belongs to `DebugOverlay`.

Gameplay objects may expose diagnostic data, but they should not write directly to the shared label.

Per-object debug widgets can live under the object that owns the data.

## Test Hooks

Do not add production-only metadata, flags, counters, or debug breadcrumbs just for tests.

Tests should observe through:

- public runtime APIs
- real gameplay effects
- test-owned listener components
- explicit diagnostic APIs that are useful outside tests

Test-only prefabs and scenes belong under `Assets/Tests/`.

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

Player and mob movement/collision should use Unity Physics2D. Projectile and AOE
hit simulation should use target snapshots and baked shapes instead of thousands
of live trigger objects.

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

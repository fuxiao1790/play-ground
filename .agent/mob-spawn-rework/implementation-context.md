# Implementation Context

## Architectural Decisions
- Replace deleted spawn code with pooled GameObject mobs driven by one SpawnController.
- MobRoot keeps one lifetime path: InitializeForSpawn for each life, SoftDie/OnDisable for teardown, no self-Destroy.
- Spawn timing and placement are ScriptableObject strategies; mutable runtime state lives outside SO assets.

## Global Invariants
- Mobs remain Physics2D GameObjects. Only target proxies enter ECS.
- No static scene mobs is valid.
- Existing GameRoot.Configure(CombatRoot, PlayerRoot, MobRoot[]) stays intact.

## Ownership Boundaries
- SpawnController and SpawnPoint are Scene-and-Authoring MonoBehaviours.
- Spawn rules, placement rules, and mob weighted table are Game-Logic ScriptableObjects/plain runtime classes.
- CombatRoot still owns target registry and ECS bridge.

## Data Flow
- Behaviour Tick -> ISpawnSink.Spawn -> table chooses prefab -> placement resolves position -> pool rents mob -> controller wires CombatRoot/target/VFX.
- MobRoot soft death raises SoftDied; SpawnController defers return to pool until Update reclaim.

## Lifecycle / Allocation Rules
- Pool root is inactive during setup so instances remain parked until rented.
- Controller prewarms during Awake with max(prewarm, cap); steady-state spawn should reuse instances.
- MobRoot InitializeForSpawn resets per-life state while active before Register.

## ECS / Job / Threading Constraints
- New spawn code must not touch ECS jobs/systems directly.
- Registering MobRoot with CombatTargetRegistry creates target proxy only when active, alive, and health > 0.

## Determinism Requirements
- Random comes from per-controller System.Random using serialized seed when nonzero.

## Producer / Consumer Separation
- MobRoot produces SoftDied event.
- SpawnController consumes SoftDied and returns mobs to MobPool.

## Reused Mechanisms
- MobRoot Register, BindCombatRoot, BindVfxRoot, SetTarget, SoftDied.
- GameRoot existing static mob registration.
- Weighted prefab selection from deleted MobSpawnPool algorithm.

## Introduced Mechanisms
- MobSpawnTable, MobPool, SpawnPoint.
- SpawnBehaviour/ContinuousStreamBehaviour runtime.
- SpawnPlacement/FixedPointPlacement.
- SpawnController implementing ISpawnSink.

## Validation Requirements
- Build/compile after tasks.
- PlayMode tests for pool reuse, cap holding, re-registration, per-life reset, and reclaim accounting.
- Explain any manual scene verification not run.

## Files / Systems Mentioned By The Plan
- Assets/Scripts/Mob/MobRoot.cs
- Assets/Scripts/Game/GameRoot.cs
- Assets/Scripts/Spawn/*.cs
- Assets/Tests/PlayMode/MobSpawnControllerPlayModeTests.cs

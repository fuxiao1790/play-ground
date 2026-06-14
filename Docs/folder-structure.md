# Folder Structure

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

Quick map.

## Docs Layout

```
Docs/
  ├── project-overview.md       meta: project vision and scope
  ├── architecture.md           meta: system ownership and runtime shape
  ├── folder-structure.md       meta: this file
  ├── coding-standards.md       meta: C# and Unity rules
  ├── hit-spawn-snapshot.md     meta: spawn safety architectural decision
  ├── performance.md            meta: performance constraints and stress tests
  ├── profiling.md              meta: profiling data reference
  ├── testing.md                meta: test strategy and coverage goals
  ├── release.md                meta: build steps
  │
  ├── design/                   Layer 1 — player-facing design
  │   └── gameplay.md
  │
  ├── game-logic/               Layer 2 — game rules and mechanics
  │   ├── skill-system.md
  │   ├── mobs.md
  │   ├── mob-behaviour.md
  │   └── spawn-system.md
  │
  └── simulation/               Layer 3 — internal ECS / simulation
      ├── projectile-system.md
      ├── aoe-system.md
      ├── ecs-notes.md
      └── vfx-system.md
```

## Current Folders

- `Assets/`: Unity assets, scenes, prefabs, scripts, art, audio, settings
- `Assets/Scenes/`: Unity scene files
- `Assets/Settings/`: URP and project settings assets
- `Docs/`: design docs (see layout above)
- `Packages/`: Unity package manifest and lock files
- `ProjectSettings/`: Unity project settings

## Target Asset Folders

Create these as systems are ported:

- `Assets/Scripts/`: runtime C# code
- `Assets/Scripts/Common/`: shared helpers, damage payloads, state machine core
- `Assets/Scripts/Player/`: player root, movement, facing, animation, attack loadout
- `Assets/Scripts/Attack/`: attack MonoBehaviours and attack authoring code
- `Assets/Scripts/Mob/`: mob root, state, triggers, behaviours, attacks
- `Assets/Scripts/Spawn/`: spawn root, spawn points, spawn config
- `Assets/Scripts/Camera/`: camera follow, mouse bias, zoom
- `Assets/Scripts/Level/`: play area wall and arena setup
- `Assets/Scripts/Audio/`: gameplay audio manager and pooled one-shots
- `Assets/Scripts/Debugging/`: shared debug overlay
- `Assets/Scripts/System/Projectile/`: scoped projectile runtime
- `Assets/Scripts/System/Common/`: shared combat ECS data, shape baking, bounds,
  and narrow-phase collision helpers used by projectile and AOE runtimes
- `Assets/Scripts/System/Aoe/`: scoped AOE runtime
- `Assets/Scripts/System/Vfx/`: optional batched VFX runtime
- `Assets/Prefabs/Player/`: player prefab and child attack loadout prefabs
- `Assets/Prefabs/Mobs/`: bat, slime, skeleton, and later mob prefabs
- `Assets/Prefabs/Attacks/`: projectile and AOE attack prefabs
- `Assets/Prefabs/Projectiles/`: authored projectile visual/collision templates
- `Assets/Prefabs/Effects/`: AOE effect visuals and transient effects
- `Assets/Prefabs/Level/`: play area and environment prefabs
- `Assets/ScriptableObjects/`: shared authored data
- `Assets/ScriptableObjects/Mobs/`: mob behavior and trigger configs
- `Assets/ScriptableObjects/Spawn/`: spawn pools and spawn tuning
- `Assets/Art/`: sprites, tiles, animation source
- `Assets/Audio/`: sound effects and music
- `Assets/Tests/EditMode/`: pure and editor tests
- `Assets/Tests/PlayMode/`: scene/runtime tests

## Good First Places To Look

- Want project summary: [project-overview.md](./project-overview.md)
- Want architecture: [architecture.md](./architecture.md)
- Want coding rules: [coding-standards.md](./coding-standards.md)
- Want player controls and game loop: [design/gameplay.md](./design/gameplay.md)
- Want skill/support system: [game-logic/skill-system.md](./game-logic/skill-system.md)
- Want projectiles: [simulation/projectile-system.md](./simulation/projectile-system.md)
- Want AOEs: [simulation/aoe-system.md](./simulation/aoe-system.md)
- Want VFX: [simulation/vfx-system.md](./simulation/vfx-system.md)
- Want mobs: [game-logic/mobs.md](./game-logic/mobs.md) and [game-logic/mob-behaviour.md](./game-logic/mob-behaviour.md)
- Want spawning: [game-logic/spawn-system.md](./game-logic/spawn-system.md)
- Want tests: [testing.md](./testing.md)
- Want ECS patterns: [simulation/ecs-notes.md](./simulation/ecs-notes.md)

## Current Projectile Runtime Map

- `Assets/Scripts/System/Common/CombatEcsComponents.cs`: shared
  `CombatKinematicsComponent`, `CombatCollisionComponent`, `CombatHitComponent`,
  and `CombatTargetElement` data used by projectile now and AOE later
- `Assets/Scripts/System/Common/CombatShapeType.cs`: shared circle, rectangle,
  and capsule shape enum
- `Assets/Scripts/System/Common/CombatTargetShapeUtility.cs`: shared collider
  shape baking from Unity `Collider2D`
- `Assets/Scripts/System/Common/CombatCollisionMath.cs`: shared bounds and
  narrow-phase collision math
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`: shared batched
  sprite render ECS data and matrix preparation for projectile and AOE visuals
- `Assets/Scripts/System/Projectile/ProjectileRoot.cs`: scene-object bridge,
  spawn request submission, target snapshot sync, hit replay, raw payload
  dispatch, counters, and batched render submission
- `Assets/Scripts/System/Projectile/ProjectileSimulationSystem.cs`: per-scope
  projectile event buffer clearing at the start of simulation
- `Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs`: per-scope
  projectile recycle-buffer draining, spawn request materialization, inactive
  entity reuse by scope/render type/slot kind, and cold entity creation through
  ECB
- `Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs`: homing target
  refresh, reacquire, and steering
- `Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs`: position
  integration
- `Assets/Scripts/System/Projectile/ProjectileChildSpawnSystem.cs`: timed child
  projectile spawn request creation
- `Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs`: lifetime
  countdown and active-state disable for expired projectiles
- `Assets/Scripts/System/Projectile/ProjectileContactGateSystem.cs`: repeat-hit
  contact gate expiry
- `Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs`: target mask
  filtering, shape hit checks, pierce, hit events, and active-state disable for
  hit-despawned projectiles
- `Assets/Scripts/System/Projectile/ProjectileCollisionMath.cs`: projectile
  compatibility adapter over shared common collision math
- projectile render submission lives in `ProjectileRoot`; shared matrix
  preparation lives in `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Current AOE Runtime Map

- `Assets/Scripts/System/Aoe/AoeRoot.cs`: scene-object bridge, AOE type baking,
  spawn request submission, target snapshot sync, hit replay, counters, and
  batched render submission
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`: AOE scope, tag, identity,
  active, spawn, hit, recycle, contact-gate, and render ECS data
- `Assets/Scripts/System/Aoe/AoeSimulationSystem.cs`: per-scope AOE hit-buffer
  clearing at the start of simulation
- `Assets/Scripts/System/Aoe/AoeSpawnSystem.cs`: per-scope AOE recycle-buffer
  draining, spawn request materialization, inactive entity reuse by scope/type,
  and cold entity creation
- `Assets/Scripts/System/Aoe/AoeCollisionSystem.cs`: target mask filtering,
  bounds/narrow-phase collision, hit events, and pulse-AOE deactivation
- AOE render submission lives in `AoeRoot`; shared matrix preparation lives in
  `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Aoe/AoeLifetimeSystem.cs`: lingering AOE lifetime
  countdown with expire deactivation and pulse VFX interval ticks

## Current VFX Runtime Map

- `Assets/Scripts/System/Vfx/VfxEcsComponents.cs`: `VfxPendingSpawn` transient
  native payload; `VfxSpawnRequestElement` scope buffer (drained by
  `CombatVfxDispatchSystem`, not by roots); `CombatScopeVfxCatalog` unmanaged
  `IComponentData` holding an opaque `int VfxRootId` — the only VFX reference
  stored in ECS; added to scope entities via `CombatVfxRoot.Bind`
- `Assets/Scripts/System/Vfx/VfxFlushJob.cs`: Burst IJob draining per-system
  `NativeQueue<VfxPendingSpawn>` into the scope buffer
- `Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs`: `VfxTypeResources` and
  `CombatVfxDispatcher`; owns VFX instances, GraphicsBuffers, staging lists,
  and the per-frame stage→upload→dispatch loop; one instance owned by
  `CombatVfxRoot` (not by `ProjectileRoot` or `AoeRoot`)
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`: scene-object MonoBehaviour;
  holds a static `Dictionary<int, CombatVfxRoot>` registry keyed by
  auto-incremented int; `Register(typeId, trigger, asset)` delegates to the
  owned `CombatVfxDispatcher`; `Bind(scopeEntity, entityManager)` upserts
  `CombatScopeVfxCatalog` onto a scope entity; `DrainAndDispatch` stages events
  from the scope buffer and calls `Dispatcher.Dispatch`; registerers
  (`PlayerSkillDriver`, `MobProjectileAttack`) hold a serialized `vfxRoot`
  reference and call both their domain root and `CombatVfxRoot.Register` —
  `ProjectileRoot` and `AoeRoot` have no dependency on `CombatVfxRoot`
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`: ECS `SystemBase` in
  `PresentationSystemGroup`; queries scope entities with `CombatScopeVfxCatalog`;
  resolves each scope's `CombatVfxRoot` via the static registry and calls
  `DrainAndDispatch`; VFX graphs remain fully encapsulated in `CombatVfxRoot`

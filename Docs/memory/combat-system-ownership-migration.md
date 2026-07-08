# Combat System Ownership Migration

## Final Folder Tree

```text
Assets/Scripts/System/
  Combat/
    Application/
    Aoes/
    Collision/
    Core/
    Lifetime/
    Platform/
    Projectiles/
    Rendering/
    Spawning/
    Stats/
    Status/
    Targets/
    Vfx/
```

No `Common`, `Shared`, `Utils`, `Helpers`, `Managers`, or `Misc` folder remains under
`Assets/Scripts/System`.

## Migration Map

| Old path | New path | Owner | Reason |
|---|---|---|---|
| `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs` | `Assets/Scripts/System/Combat/Application/CombatApplyFinalizeSingleSystem.cs` | Application | Applies hit events, updates target health/status, and hands finalized combat ticks to presentation. |
| `Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs.delete` | `Assets/Scripts/System/Combat/Application/CombatApplyFinalizeSystem.cs.delete` | Application | Retired apply/finalize reference belongs beside active finalization code. |
| `Assets/Scripts/System/Common/CombatHitEvent.cs` | `Assets/Scripts/System/Combat/Application/CombatHitEvent.cs` | Application | Raw hit event contract exists for hit application/finalization. |
| `Assets/Scripts/System/Common/CombatEcsComponents.cs` | `Assets/Scripts/System/Combat/Application/CombatHitPayload.cs` | Application | Fire-time hit payload is consumed by collision and finalization, so damage application owns it. |
| `Assets/Scripts/System/Common/CombatTargetProxy.cs` | `Assets/Scripts/System/Combat/Targets/CombatTargetProxy.cs` | Targets | Creates, updates, and deletes ECS target proxy entities. |
| `Assets/Scripts/System/Common/CombatTargetRegistry.cs` | `Assets/Scripts/System/Combat/Targets/CombatTargetRegistry.cs` | Targets | Managed target registry owns target-to-proxy binding. |
| `Assets/Scripts/System/Common/CombatTargetSet.cs` | `Assets/Scripts/System/Combat/Targets/CombatTargetSet.cs` | Targets | Target set snapshots target-owned collision and faction state. |
| `Assets/Scripts/System/Common/ICombatTarget.cs` | `Assets/Scripts/System/Combat/Targets/ICombatTarget.cs` | Targets | Target callback and target shape/health contract. |
| `Assets/Scripts/System/Collision/TargetCollisionShape.cs` | `Assets/Scripts/System/Combat/Targets/TargetCollisionShape.cs` | Targets | Target-owned collision shape state is authored by target proxies. |
| `Assets/Scripts/System/Projectile/*` | `Assets/Scripts/System/Combat/Projectiles/*` | Projectiles | Projectile behavior, components, movement, tracking, spawning, and projectile collision use cases. |
| `Assets/Scripts/System/Common/ProjectileTrackingConfig.cs` | `Assets/Scripts/System/Combat/Projectiles/ProjectileTrackingConfig.cs` | Projectiles | Projectile-only tracking authoring/runtime config. |
| `Assets/Scripts/System/Aoe/*` | `Assets/Scripts/System/Combat/Aoes/*` | Aoes | AOE behavior, shape authoring, spawning, collision use cases, registry, and pulse VFX triggers. |
| `Assets/Scripts/System/Collision/CollisionConstants.cs` | `Assets/Scripts/System/Combat/Collision/CollisionConstants.cs` | Collision | Shared collision caps and collision buffer constants. |
| `Assets/Scripts/System/Collision/CombatCollisionComponents.cs` | `Assets/Scripts/System/Combat/Collision/CombatCollisionComponents.cs` | Collision | Shared collision component, collision-active gate, and collision target snapshot buffer. |
| `Assets/Scripts/System/Collision/CombatCollisionMath.cs` | `Assets/Scripts/System/Combat/Collision/CombatCollisionMath.cs` | Collision | Shared bounds and narrow-phase collision math. |
| `Assets/Scripts/System/Collision/CombatShapeType.cs` | `Assets/Scripts/System/Combat/Collision/CombatShapeType.cs` | Collision | Shared shape primitive enum used by collision math. |
| `Assets/Scripts/System/Collision/CombatSpatialHash.cs` | `Assets/Scripts/System/Combat/Collision/CombatSpatialHash.cs` | Collision | Shared spatial hash cell/key infrastructure. |
| `Assets/Scripts/System/Collision/CombatTargetShapeUtility.cs` | `Assets/Scripts/System/Combat/Collision/CombatTargetShapeUtility.cs` | Collision | Collider-to-combat-shape conversion supports shared collision shape extraction. |
| `Assets/Scripts/System/Collision/TargetSpatialHashSystem.cs` | `Assets/Scripts/System/Combat/Collision/TargetSpatialHashSystem.cs` | Collision | Shared target broadphase consumed by projectile, tracking, and AOE collision systems. |
| `Assets/Scripts/System/Common/SpawnTemplateComponents.cs` | `Assets/Scripts/System/Combat/Spawning/SpawnTemplateComponents.cs` | Spawning | Cross-feature spawn template registries for projectile and AOE child spawns. |
| `Assets/Scripts/System/Common/IntervalChildTemplates.cs` | `Assets/Scripts/System/Combat/Spawning/IntervalChildTemplates.cs` | Spawning | Cross-feature child spawn kind/reference contract. |
| `Assets/Scripts/System/Common/TimedSpawnSystem.cs` | `Assets/Scripts/System/Combat/Spawning/TimedSpawnSystem.cs` | Spawning | Generic timed child spawn producer for projectile and AOE events. |
| `Assets/Scripts/System/Common/CombatEcsComponents.cs` | `Assets/Scripts/System/Combat/Spawning/TimedSpawnComponents.cs` | Spawning | Timed spawn component/state exist for cross-feature child spawn flow. |
| `Assets/Scripts/System/Common/CombatArmingSystem.cs` | `Assets/Scripts/System/Combat/Lifetime/CombatArmingSystem.cs` | Lifetime | Shared arming/windup state controls transient combat entity activation. |
| `Assets/Scripts/System/Common/CombatLifetimeSystem.cs` | `Assets/Scripts/System/Combat/Lifetime/CombatLifetimeSystem.cs` | Lifetime | Shared transient projectile/AOE lifetime expiry. |
| `Assets/Scripts/System/Common/CombatPoolCleanupSystem.cs` | `Assets/Scripts/System/Combat/Lifetime/CombatPoolCleanupSystem.cs` | Lifetime | Reusable transient entity pool cleanup. |
| `Assets/Scripts/System/Common/CombatDeathUtility.cs` | `Assets/Scripts/System/Combat/Lifetime/CombatDeathUtility.cs` | Lifetime | Disables transient entity active/render/collision/VFX state on death/despawn. |
| `Assets/Scripts/System/Common/CombatEcsComponents.cs` | `Assets/Scripts/System/Combat/Lifetime/CombatLifecycleComponents.cs` | Lifetime | Active, arming, lifetime, and transient kinematics live on reusable combat entities. |
| `Assets/Scripts/System/Rendering/CombatRenderComponents.cs` | `Assets/Scripts/System/Combat/Rendering/CombatRenderComponents.cs` | Rendering | Combat sprite render data, registry, GPU resources, and matrix helper. |
| `Assets/Scripts/System/Rendering/CombatRenderPrepareSystem.cs` | `Assets/Scripts/System/Combat/Rendering/CombatRenderPrepareSystem.cs` | Rendering | Prepares render matrices for combat sprite submission. |
| `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs` | `Assets/Scripts/System/Combat/Rendering/CombatBatchedRenderSystem.cs` | Rendering | Batched combat sprite draw submission. |
| `Assets/Scripts/System/Vfx/*` | `Assets/Scripts/System/Combat/Vfx/*` | Vfx | Combat VFX dispatch data, root, dispatcher, dispatch system, and preview driver. |
| `Assets/Scripts/System/Status/*` | `Assets/Scripts/System/Combat/Status/*` | Status | Stack snapshots and status processing. |
| `Assets/Scripts/System/Stats/*` | `Assets/Scripts/System/Combat/Stats/*` | Stats | Combat diagnostics counters, binding, gather, and reset systems. |
| `Assets/Scripts/System/Common/CombatRoot.cs` | `Assets/Scripts/System/Combat/Core/CombatRoot.cs` | Core | Combat root/bootstrap coordinates the whole combat runtime. |
| `Assets/Scripts/System/Common/CombatScope.cs` | `Assets/Scripts/System/Combat/Core/CombatScope.cs` | Core | Combat-wide scope and faction primitives. |
| `Assets/Scripts/System/Common/CombatEcsComponents.cs` | `Assets/Scripts/System/Combat/Core/CombatScopeOwner.cs` | Core | Shared combat scope lifetime and scope bootstrap. |
| `Assets/Scripts/System/Common/CombatEcsComponents.cs` | `Assets/Scripts/System/Combat/Platform/CombatEcsWorld.cs` | Platform | Unity ECS world acquire/release and player-loop integration. |

## Common File Justification

Every file formerly under `Assets/Scripts/System/Common` was moved to a narrower
owner. `Core` only kept bootstrap/scope primitives. Target API/state moved to
`Targets`; hit events/application moved to `Application`; spawn registries and
timed child spawning moved to `Spawning`; transient active/arming/lifetime/pool
state moved to `Lifetime`; rendering moved to `Rendering`; projectile-only
tracking config moved to `Projectiles`; ECS world integration moved to
`Platform`.

## Shared Destination Ownership Answers

- `Application`: owned by combat result application. Projectiles, AOEs, and
  status consume it by producing hit events or reading finalized results. It is
  not owned by those features because it applies aggregate results across all hit
  producers. Move if finalization becomes feature-specific.
- `Targets`: owned by target representation and proxy lifecycle. Projectiles,
  AOEs, status, and application consume target state. It is not owned by a hit
  producer because target proxy lifetime is actor/target-side state. Move if a
  target type becomes feature-specific.
- `Collision`: owned by shared collision infrastructure. Projectile, AOE, and
  tracking systems consume it. It is not owned by one feature because it supplies
  shared shape math and broadphase infrastructure. Move feature-specific
  collision behavior down into that feature.
- `Spawning`: owned by cross-feature spawn machinery. Projectiles, AOEs, and
  timed child spawns consume it. Move a file down if it becomes projectile-only
  or AOE-only.
- `Lifetime`: owned by reusable transient combat entity lifecycle. Projectiles,
  AOEs, rendering, collision, and VFX observe active/arming/lifetime state. Move
  a file down if lifetime behavior diverges by feature.
- `Rendering`: owned by combat sprite rendering and GPU submission. Projectile
  and AOE visuals consume it. Move a file down if it becomes feature-specific
  visual behavior rather than shared render submission.
- `Vfx`: owned by combat VFX dispatch integration. Projectile, AOE, status, and
  lifetime systems enqueue VFX. Move only if a VFX path becomes feature-local.
- `Status`: owned by status stack simulation. Application, projectile, and AOE
  flows consume status payloads. Move if a status payload becomes only a hit
  application concern.
- `Stats`: owned by diagnostics. Combat systems write/read counters. Move if a
  metric becomes private to one feature.
- `Core`: owned by combat-wide bootstrap and scope primitives. Features consume
  scope/faction/root APIs. Move any non-bootstrap behavior out of Core.
- `Platform`: owned by Unity/ECS integration. CombatRoot consumes it. Move if ECS
  world ownership becomes a broader non-combat platform concern.

## Difficult Classifications

- `CombatKinematicsComponent` is under `Lifetime` because it is part of the
  reusable transient entity archetype reset/activation contract. If a future
  shared `Movement` owner exists, move it there.
- `CombatRoot` remains in `Core` because it bootstraps the whole combat runtime,
  even though it still contains projectile, AOE, render, target, and VFX
  registration code.
- `TargetCollisionShape` moved to `Targets`, not `Collision`, because it is
  target-owned shape state. `Collision` only consumes it for broadphase.

## Later Splits

- Split `CombatRoot` into feature registration collaborators owned by
  `Projectiles`, `Aoes`, `Rendering`, `Targets`, and `Vfx`.
- Split `CombatApplyFinalizeSingleSystem.cs` into dispatch singleton,
  finalization system, bridge, and result DTO files.
- Split `CombatRenderComponents.cs` into render components, resource registry,
  and matrix utility.
- Split large spawn apply/expansion systems once ownership is stable.

## Namespace Changes

- `PlayGround.System.Common` was removed for combat ECS code.
- `PlayGround.System.Aoe` became `PlayGround.System.Combat.Aoes`.
- `PlayGround.System.Projectile` became `PlayGround.System.Combat.Projectiles`.
- `PlayGround.System.Vfx` became `PlayGround.System.Combat.Vfx`.
- `PlayGround.System.Stats` became `PlayGround.System.Combat.Stats`.
- New owner namespaces: `Application`, `Collision`, `Core`, `Lifetime`,
  `Platform`, `Rendering`, `Spawning`, `Status`, and `Targets` under
  `PlayGround.System.Combat`.

## Compile Status

- Static scans show no old `PlayGround.System.Common`, `PlayGround.System.Aoe`,
  `PlayGround.System.Projectile`, `PlayGround.System.Vfx`, or
  `PlayGround.System.Stats` references in scripts/docs.
- Static scans show no forbidden `Common`, `Shared`, `Utils`, `Helpers`,
  `Managers`, or `Misc` folders under `Assets/Scripts/System`.
- `dotnet build PlayGround.Runtime.csproj` is blocked by Unity
  RenderPipelines Core package-cache errors before project code builds.
- `dotnet build PlayGround.Runtime.csproj /p:BuildProjectReferences=false` is
  blocked by generated `.csproj` source paths that Unity has not regenerated.
- Unity batchmode compile/regeneration could not run while another Unity
  instance has this project open.

# ECS Simulation

## Purpose

Own high-count combat runtime data and scalable simulation: projectiles, AOEs,
target proxy reads, hit qualification, health/status aggregation, spawn
expansion/apply, pooling, timed spawns, VFX request production, and render data
preparation.

## Owns

- Projectile and AOE entities and their ECS component state.
- `ProjectileSpawnEvent`/`AOE variant spawn event` drain from native queues and scope
  buffers.
- `ProjectileSpawnCommand`/`AoeSpawnCommand` production and consumption.
- `Active`-based reuse and cold creation fallback.
- `CombatHitEvent` production and ECS-owned `CombatTickResult` finalization.
- `TargetHealth`, `TargetStackEntry`, and status processing.
- VFX request production as data.
- Render matrix preparation.

## Does Not Own

- ScriptableObject or prefab authoring semantics.
- GameObject movement, wall/body collision, animation, or actor lifetime.
- Managed target callback resolution.
- VFX Graph object ownership or final draw submission.

## Inputs

- Spawn events from the combat bridge and internal ECS producers.
- Unmanaged target proxy data.
- Runtime component snapshots and native event queues/streams.
- Time step data and ECS query results.

## Outputs

- Projectile/AOE ECS entities and reusable slots.
- Hit, status, spawn follow-up, and VFX request data.
- `CombatTickResult` values for presentation.
- Render batch matrices and render grouping data.

## Allowed Dependencies

- May read documented ECS contracts:
  [Spawn Events And Commands](../contracts/spawn-events-and-commands.md),
  [Target Proxy](../contracts/target-proxy.md),
  [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md).
- May produce [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md),
  [VFX Requests](../contracts/vfx-requests.md), and
  [Render Batch Data](../contracts/render-batch-data.md).

## Forbidden Dependencies

- Must not read GameObjects, Transforms, Colliders, Physics2D, or live
  ScriptableObjects.
- Must not read managed `TargetCompanion` values.
- Must not call `ICombatTarget` callbacks.
- Must not use `Active`, common combat components, or `CombatScope` membership
  as domain markers without `ProjectileTag` or `AoeTag`.

## Main Systems / Modules

- `Assets/Scripts/System/Combat/Projectiles/`
- `Assets/Scripts/System/Combat/Aoes/`
- `Assets/Scripts/System/Combat/Collision/`
- `Assets/Scripts/System/Combat/Status/`
- `Assets/Scripts/System/Combat/Application/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Combat/Lifetime/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Combat/Spawning/TimedSpawnSystem.cs`
- `Assets/Scripts/System/Combat/Rendering/CombatRenderComponents.cs`

## Related Contracts

- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Target Proxy](../contracts/target-proxy.md)
- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [Skill Runtime Snapshots](../contracts/skill-runtime-snapshots.md)
- [VFX Requests](../contracts/vfx-requests.md)
- [Render Batch Data](../contracts/render-batch-data.md)

## Related Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)
- [VFX Dispatch](../flows/vfx-dispatch.md)

## Notes / TODOs

- Detailed references:
  [index.md](../reference/simulation/index.md),
  [projectile-system.md](../reference/simulation/projectile-system.md),
  [aoe-system.md](../reference/simulation/aoe-system.md),
  [spawn-template-registry.md](../reference/simulation/spawn-template-registry.md),
  [ecs-notes.md](../reference/simulation/ecs-notes.md).

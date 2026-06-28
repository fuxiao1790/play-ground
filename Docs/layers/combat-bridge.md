# Combat Bridge

## Purpose

Own the managed boundary between Unity scene/game logic and ECS combat
simulation. The bridge validates managed submissions, owns target registries,
registers render/VFX resources, and appends spawn intent to ECS-accessible
buffers.

## Owns

- One faction-agnostic `CombatRoot`; faction is a per-spawn argument, not a per-root property.
- `CombatTargetRegistry` for managed target registration.
- `CombatTargetProxy` creation, push, and deletion entry points.
- Ref-counted ECS world/scope acquisition and release through combat ownership
  helpers.
- Projectile/AOE type registration and render resource dictionaries.
- Managed spawn submission into shared `CombatScope` buffers.

## Does Not Own

- Spawn expansion math.
- Entity reuse or cold creation.
- Projectile/AOE simulation, collision, status processing, or render matrix
  preparation.
- Final target health/status authority after hit aggregation.

## Inputs

- Managed spawn requests from game logic.
- Target registration from actor roots.
- Authored projectile, AOE, render, and VFX configuration.

## Outputs

- `ProjectileSpawnEvent` and `AoeSpawnEvent` values on shared scope buffers.
- Target proxy entities and target proxy updates.
- Registered render/VFX catalog data visible to presentation systems.

## Allowed Dependencies

- May depend on scene objects and authoring data during validation and
  registration.
- May write documented [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
  event buffers.
- May produce [Target Proxy](../contracts/target-proxy.md) data.
- May expose [Combat Root API](../contracts/combat-root-api.md).

## Forbidden Dependencies

- Must not perform high-count collision loops.
- Must not call managed target damage callbacks from collision-time data.
- Must not create projectile/AOE entities directly from managed game logic.
- Must not treat `CombatScope` membership as domain or faction.

## Main Systems / Modules

- `Assets/Scripts/System/Common/CombatRoot.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Common/CombatScope.cs`
- `Assets/Scripts/System/Common/CombatTargetRegistry.cs`
- `Assets/Scripts/System/Common/CombatTargetProxy.cs`

## Related Contracts

- [Combat Root API](../contracts/combat-root-api.md)
- [Spawn Requests](../contracts/spawn-requests.md)
- [Spawn Events And Commands](../contracts/spawn-events-and-commands.md)
- [Target Proxy](../contracts/target-proxy.md)
- [Render Batch Data](../contracts/render-batch-data.md)

## Related Flows

- [Skill To Combat Spawn](../flows/skill-to-combat-spawn.md)
- [Spawn Event To Entity](../flows/spawn-event-to-entity.md)
- [Target Proxy Lifecycle](../flows/target-proxy-lifecycle.md)

## Notes / TODOs

- Detailed bridge reference:
  [project-aoe-system-common.md](../reference/simulation/project-aoe-system-common.md).

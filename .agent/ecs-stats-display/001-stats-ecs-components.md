# 001 — Stats ECS components

## Goal
Define the singleton component holding all stat fields and the managed binding
component that links the entity to the display GameObject.

## Files
- New folder `Assets/Scripts/System/Stats/`.
- New file `Assets/Scripts/System/Stats/CombatStatsComponents.cs`.

## Content

```csharp
namespace PlayGround.System.Stats
{
    // ECS Lifecycle: singleton data; created once by CombatStatsGatherSystem.OnCreate;
    // overwritten each frame by that system; never removed (dies with the world).
    public struct CombatStatsSingleton : Unity.Entities.IComponentData
    {
        public int EntitiesSpawnedViaEcb;    // cold create (new archetype entity)
        public int EntitiesSpawnedViaReuse;  // claimed a disabled Active slot
        public int HitEventsCreated;         // CombatHitEvents finalized this frame
        public int VfxEventsCreated;         // VfxPendingSpawns dispatched this frame
    }

    // ECS Lifecycle: managed singleton binding; created with the singleton entity;
    // Display is set/cleared by CombatStatsDisplay via CombatStatsGatherSystem.Bind/Unbind.
    public sealed class CombatStatsBinding : Unity.Entities.IComponentData
    {
        public CombatStatsDisplay Display;
    }
}
```

## Notes
- `CombatStatsBinding` is a `class` so it is a managed component (mirrors
  `CombatRenderResourceRegistry`). It forward-references `CombatStatsDisplay`
  (subtask 004); both live in the same assembly (`PlayGround.Runtime`).
- Use the `ECS Lifecycle:` comment prefix as required by coding-standards.
- All four fields are per-frame instantaneous counts.

## Acceptance criteria
- Project compiles with the new component types in `PlayGround.System.Stats`.
- `CombatStatsSingleton` is unmanaged (struct, blittable ints) so it works with
  `SystemAPI.SetSingleton` / `SetComponentData`.

## Dependencies
None (but `CombatStatsDisplay` referenced here is created in 004; compile the set
together).

## Scope
Small.

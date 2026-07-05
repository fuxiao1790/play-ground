# 001 — ECS: AoeDelayComponent + archetypes + command field

## Goal
Introduce the windup timer component, put it on both AOE archetypes, and add the
authored delay to the spawn command.

## Changes
### `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`
Add:
```csharp
// ECS Lifecycle: enableable AOE windup timer; added at entity creation; kept until
// root teardown; enabled while the AOE is in its initial-delay windup (collision +
// lifetime + interval spawns suppressed); disabled by AoeDelaySystem on expiry,
// which activates the AOE. Never enabled when InitialDelaySeconds <= 0.
public struct AoeDelayComponent : IComponentData, IEnableableComponent
{
    public float Remaining;
    // Baked at apply from the command so AoeDelaySystem knows what to turn on at
    // activation without re-deriving predicates: 1 => enable AoeCollisionActiveTag,
    // 1 => enable TimedSpawnComponent (lingering only).
    public byte ActivateCollision;
    public byte ActivateTimedSpawn;
}
```

### `Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs` (`AoeSpawnCommand`)
Add field:
```csharp
public float InitialDelaySeconds;
```
Place near `Lifetime` / `RepeatHitCooldownSeconds` for readability.

### `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs` (archetypes)
Add `typeof(AoeDelayComponent)` to **both** `_impactArchetype` (in
`ImpactAoeSpawnApplySystem.OnCreate`) and `_lingeringArchetype` (in
`LingeringAoeSpawnApplySystem.OnCreate`).

## Acceptance criteria
- Project compiles.
- Existing dead-slot queries unchanged; adding the component does not alter their
  matching (verify impact `_deadSlotQuery` still `WithNone<CombatLifetimeComponent>`
  and lingering still `WithAll<CombatLifetimeComponent>`).
- Newly created AOEs have `AoeDelayComponent` present; it must be **disabled by
  default** at creation (set explicitly in 002; new enableable components default
  to enabled on `CreateEntity`, so 002 must disable it on the non-windup path).

## Dependencies
None. Precedes 002/003.

## Scope
Small.

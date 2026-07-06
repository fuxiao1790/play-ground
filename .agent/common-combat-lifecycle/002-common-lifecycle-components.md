# 002 - Common Lifecycle Components

## Goal

Add shared lifecycle data to projectile and AOE reusable archetypes without
changing current behavior.

Proposed data:

```csharp
public enum CombatLifecyclePhase : byte
{
    Arming = 0,
    Armed = 1
}

public struct CombatLifecycleComponent : IComponentData
{
    public CombatLifecyclePhase Phase;
    public float ArmingRemaining;
    public byte PendingDeath;
}

public struct CombatArmedDeltaComponent : IComponentData
{
    public float Value;
}
```

`Dead` stays represented by disabled `Active`.

## Scope

- Add common lifecycle data under `Assets/Scripts/System/Common/`.
- Add lifecycle and armed-delta components to:
  - projectile archetype
  - impact AOE archetype
  - lingering AOE archetype
- Reset lifecycle data on reuse and cold-create paths.
- Current zero-arming spawns initialize as `Armed`, `ArmingRemaining = 0`,
  `PendingDeath = 0`.
- `CombatArmedDeltaComponent.Value` is reset each lifecycle tick.

## Acceptance Criteria

- All projectile/AOE reusable entities carry the common lifecycle data.
- Existing behavior remains unchanged before systems consume the new data.
- Common lifecycle systems/jobs are tag-scoped by `ProjectileTag` or `AoeTag`.
- No external spawner or `CombatRoot` passes phase or enabled-gate state.

## Dependencies

001 preferred first, so lifecycle docs can refer to sprite gate by final name.

## Estimated Scope

Medium. Adds archetype components and reset writes across spawn apply paths.

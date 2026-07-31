# 001 — Event Structs & Scope Buffer Wiring

## Change

New file `Assets/Scripts/System/Targets/TargetProxyEvents.cs`
(`PlayGround.System.Combat.Targets` namespace, `// ECS Lifecycle: ...` doc-comment
convention matching `Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs`):

```csharp
public struct TargetProxyCreateEvent : IBufferElementData
{
    public int Token;                  // CombatTargetProxy-minted, NOT ICombatTarget.TargetId
    public CombatFaction Faction;
    public TargetPosition Position;
    public TargetCollisionShape Shape;
    public float MaxHealth;
    public float CurrentHealth;
    public float HealthRegenPerSecond;
    public float MaxMana;
    public float CurrentMana;
    public float ManaRegenPerSecond;
}

public enum TargetProxyUpdateKind : byte { Push, PushResourceMaxes, SetHealth, SetMana }

public struct TargetProxyUpdateEvent : IBufferElementData
{
    public TargetProxyUpdateKind Kind;
    public Entity Proxy;
    public TargetPosition Position;     // Push
    public TargetCollisionShape Shape;  // Push
    public float MaxHealth;             // PushResourceMaxes
    public float HealthRegenPerSecond;  // PushResourceMaxes
    public float MaxMana;               // PushResourceMaxes
    public float ManaRegenPerSecond;    // PushResourceMaxes
    public float CurrentValue;          // SetHealth / SetMana
}

public struct TargetProxyDeleteEvent : IBufferElementData
{
    public Entity Proxy;
}
```

Modify `Assets/Scripts/System/Core/CombatScopeOwner.cs` `Acquire(...)`: add
`entityManager.AddBuffer<TargetProxyCreateEvent>(ownedScope)`,
`AddBuffer<TargetProxyUpdateEvent>(ownedScope)`,
`AddBuffer<TargetProxyDeleteEvent>(ownedScope)` alongside the existing four
`AddBuffer` calls (lines 53-57).

## Why one `TargetProxyUpdateEvent` type, not four

`SetHealth`'s current synchronous implementation calls `PushResourceMaxes(...)` first,
then clamps `Current` against the just-updated `Max` (`CombatTargetProxy.cs:189-192`;
`SetMana` mirrors this at 206-209). Keeping `Push`/`PushResourceMaxes`/`SetHealth`/`SetMana`
as one tagged union in one FIFO buffer, drained by one system in append order,
reproduces that ordering for free. Splitting into separate buffers/systems would require
manually reconstructing "maxes applied before this clamp reads them," with no natural
ordering guarantee between separate buffers.

## Acceptance Criteria

- New file compiles; all three structs are unmanaged (no managed fields) — verify by
  confirming `IBufferElementData` constraint is satisfied (Unity will fail to compile
  otherwise).
- `CombatScopeOwner.Acquire` creates all three new buffers on the owned scope entity,
  alongside the existing four.
- No other file changes yet — this task only adds types and wires storage; nothing
  reads or writes these buffers until 002/003.

## Dependencies

None — foundational task.

## Scope

Small. One new file (~40 lines), one three-line addition to an existing file.

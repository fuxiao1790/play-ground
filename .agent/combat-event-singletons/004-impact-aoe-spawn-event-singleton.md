# 004 — Impact AOE Spawn-Event Singleton (Flavor A + B)

## Goal

Same relocation as 003, for the impact AOE lane. Introduce `ImpactAoeSpawnEventSingleton`
carrying the inbound `EventQueue` and the outbound `ImpactCommands` + handles.

Depends on: 003 (identical shape; 001/002 conventions). May be co-implemented with 005 in
one PR to touch the shared collision producers once.

## Singleton shape

```csharp
// ECS Lifecycle: singleton impact-AOE spawn lane; EventQueue + Commands created by
// ImpactAoeSpawnExpansionSystem on create, drained/produced each simulation update, consumed by
// ImpactAoeSpawnApplySystem, disposed by ImpactAoeSpawnExpansionSystem on destroy.
public struct ImpactAoeSpawnEventSingleton : IComponentData
{
    public NativeQueue<ImpactAoeSpawnEvent> EventQueue;
    public NativeList<AoeSpawnCommand> Commands;      // was ImpactCommands
    public JobHandle ProducerHandle;
    public JobHandle PendingHandle;
}
```

## Scope / files

**Sink** — `ImpactAoeSpawnExpansionSystem` in
[AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs)
(lines ~156-264): relocate `EventQueue`/`ImpactCommands`/`ProducerHandle`/`PendingHandle`
into the singleton exactly as 003 does for projectiles. Keep:
- the `DynamicBuffer<ImpactAoeSpawnEvent>` scope drain unchanged;
- the **VfxPending write** already migrated in 001 (this system is also a VFX producer) —
  leave that as-is.

**Flavor-A producers** (`ImpactAoeEventWriter` / `impactAoeExpansion.EventQueue`):
- `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`
- `StatusProcessSystem`, `TimedSpawnSystem`

**Flavor-B consumer** — `ImpactAoeSpawnApplySystem` in
[AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs) (lines ~56-71):
replace `GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>()` +
`expansionSys.PendingHandle` + `expansionSys.ImpactCommands` with the singleton read.

## Acceptance criteria

- No `GetExistingSystemManaged<ImpactAoeSpawnExpansionSystem>()` remains.
- Expansion system exposes no `internal` lane fields (VFX already handled in 001).
- Impact AOEs spawn on hit and via timed spawner; windup/telegraph timing unchanged.
- `AoeSimulationTests` cross-frame on-hit AOE assertions green.
- Determinism + Jobs Debugger + Leak Detection clean.

## Scope estimate

Small–medium (mechanical clone of 003).

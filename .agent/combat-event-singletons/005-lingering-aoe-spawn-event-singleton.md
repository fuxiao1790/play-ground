# 005 — Lingering AOE Spawn-Event Singleton (Flavor A + B)

## Goal

Same relocation as 003/004, for the lingering AOE lane. Introduce
`LingeringAoeSpawnEventSingleton` carrying the inbound `EventQueue` and outbound
`LingeringCommands` + handles.

Depends on: 003 (identical shape). May be co-implemented with 004.

## Singleton shape

```csharp
// ECS Lifecycle: singleton lingering-AOE spawn lane; EventQueue + Commands created by
// LingeringAoeSpawnExpansionSystem on create, drained/produced each simulation update, consumed
// by LingeringAoeSpawnApplySystem, disposed by LingeringAoeSpawnExpansionSystem on destroy.
public struct LingeringAoeSpawnEventSingleton : IComponentData
{
    public NativeQueue<LingeringAoeSpawnEvent> EventQueue;
    public NativeList<AoeSpawnCommand> Commands;      // was LingeringCommands
    public JobHandle ProducerHandle;
    public JobHandle PendingHandle;
}
```

## Scope / files

**Sink** — `LingeringAoeSpawnExpansionSystem` in
[AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs)
(lines ~310-418): relocate lane fields into the singleton. Keep the scope
`DynamicBuffer<LingeringAoeSpawnEvent>` drain and the VfxPending write (migrated in 001) intact.

**Flavor-A producers** (`LingeringAoeEventWriter` / `lingeringAoeExpansion.EventQueue`):
- `ProjectileCollisionSystem`, `ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`
- `StatusProcessSystem`, `TimedSpawnSystem`

**Flavor-B consumer** — `LingeringAoeSpawnApplySystem` in
[AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs) (lines ~288-303):
replace `GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>()` +
`expansionSys.PendingHandle` + `expansionSys.LingeringCommands` with the singleton read.

## Acceptance criteria

- No `GetExistingSystemManaged<LingeringAoeSpawnExpansionSystem>()` remains.
- Expansion system exposes no `internal` lane fields.
- Lingering AOEs spawn, persist for lifetime, pulse, and run timed sub-spawners correctly.
- `AoeSimulationTests` (lingering/pulse/timed-spawn) green.
- Determinism + Jobs Debugger + Leak Detection clean.

## Scope estimate

Small–medium (mechanical clone of 003).

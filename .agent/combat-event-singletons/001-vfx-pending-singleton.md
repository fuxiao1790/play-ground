# 001 — VfxPending Singleton (Proof of Concept)

## Goal

Relocate `CombatVfxDispatchSystem`'s `NativeQueue<VfxPendingSpawn> PendingSpawns` and its
`JobHandle ProducerHandle` into a new singleton `CombatVfxDispatchSingleton`, accessed via
`SystemAPI`. Migrate every VFX producer off `GetExistingSystemManaged<CombatVfxDispatchSystem>()`.
This lane is the smallest (write-only fan-in, single consumer, no Flavor-B command handoff),
so it proves the pattern and the conventions the later tasks copy.

## Scope / files

**Sink** — [CombatVfxDispatchSystem.cs](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs):
- Add:
  ```csharp
  // ECS Lifecycle: singleton VFX dispatch queue; created by CombatVfxDispatchSystem on
  // create, drained every presentation update, disposed by CombatVfxDispatchSystem on destroy.
  public struct CombatVfxDispatchSingleton : IComponentData
  {
      public NativeQueue<VfxPendingSpawn> PendingSpawns;
      public JobHandle ProducerHandle;
  }
  ```
- `OnCreate`: create `PendingSpawns`, then create a singleton entity holding the component
  (mirror `CombatStatsGatherSystem.OnCreate`). Store the queue in the singleton, not a field.
- `OnDestroy`: `GetSingleton` → `ProducerHandle.Complete()` → dispose queue (same order as today).
- `OnUpdate`: read the queue from the singleton; keep the drain/dispatch logic identical.
- Remove the `internal NativeQueue<...> PendingSpawns`, `internal JobHandle ProducerHandle`,
  `internal bool HasQueue`, `internal ... AsParallelWriter()` members once producers no longer
  use them.

**Producers** — swap `GetExistingSystemManaged<CombatVfxDispatchSystem>()` + `vfx.AsParallelWriter()`
+ `vfx.HasQueue` + `vfx.ProducerHandle = CombineDependencies(...)` for the singleton form:
```csharp
bool hasVfx = SystemAPI.TryGetSingletonRW<CombatVfxDispatchSingleton>(out var vfxRW);
// job field: VfxPending = hasVfx ? vfxRW.ValueRO.PendingSpawns.AsParallelWriter() : default
// after schedule:
if (hasVfx)
    vfxRW.ValueRW.ProducerHandle = JobHandle.CombineDependencies(vfxRW.ValueRW.ProducerHandle, handle);
```
Files to migrate:
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs) (VfxPending only)
- [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs) (VfxPending only)
- [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs) (VfxPending only)
- [AoeSpawnExpansionSystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs) — Impact + Lingering expansion (lines ~237-260, ~397-415)
- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs)
- [AoePulseVfxSystem.cs](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs)

> In the collision systems, touch **only** the VFX writer/handle wiring this task; leave the
> hit/spawn-event reach-ins for tasks 002–005.

## Acceptance criteria

- No `GetExistingSystemManaged<CombatVfxDispatchSystem>()` remains in the codebase.
- `CombatVfxDispatchSystem` exposes no `internal` queue/handle/writer members.
- VFX still dispatches (manual play: on-hit + pulse + expansion telegraph VFX appear).
- All PlayMode tests green (esp. any VFX/dispatch count assertions).
- Editor run with Jobs Debugger + Leak Detection on: no safety errors, no leaks on exit.
- Determinism unchanged; drain timing unchanged.

## Conventions this task establishes (copied by 002–005)

- Singleton created in the sink's `OnCreate`; disposed in `OnDestroy` after completing handles.
- `ECS Lifecycle:` comment on every singleton declaration.
- Producers use `TryGetSingletonRW` for null-tolerant no-op parity.
- Handle mutation stays on the main thread, post-schedule, via `RefRW.ValueRW`.

## Scope estimate

Medium. One sink + seven producer edits, but each edit is a mechanical swap. The bulk of
the value is nailing the convention.

# 003 — Projectile Spawn-Event Singleton (Flavor A + B)

## Goal

Relocate the projectile spawn lane's shared state from `ProjectileSpawnExpansionSystem`
into `ProjectileSpawnEventSingleton`, covering **both** the inbound event queue (Flavor A)
and the outbound command handoff to the apply system (Flavor B).

Depends on: 001, 002 (conventions). First lane carrying the command handoff.

## Singleton shape

```csharp
// ECS Lifecycle: singleton projectile spawn lane; EventQueue + Commands created by
// ProjectileSpawnExpansionSystem on create. EventQueue is filled by producers each frame and
// drained by the expansion system; Commands is (re)allocated per frame by the expansion job and
// consumed by ProjectileSpawnApplySystem. Disposed by ProjectileSpawnExpansionSystem on destroy.
public struct ProjectileSpawnEventSingleton : IComponentData
{
    public NativeQueue<ProjectileSpawnEvent> EventQueue;          // Flavor A inbound
    public NativeList<ProjectileSpawnCommand> Commands;           // Flavor B outbound
    public JobHandle ProducerHandle;                              // producers -> expansion drain
    public JobHandle PendingHandle;                               // expansion job -> apply read
}
```

`Commands` is `TempJob`-allocated and disposed each frame in the current code
([ProjectileSpawnExpansionSystem.cs:52-116](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs)).
Keep that per-frame reallocate/dispose lifecycle; only its storage location moves to the
singleton. Write the new `Commands`/`PendingHandle` back into the singleton via
`GetSingletonRW` at the same points the fields are assigned today.

## Scope / files

**Sink/producer of commands** — [ProjectileSpawnExpansionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs):
- `OnCreate`: create `EventQueue`, create singleton entity. (`Commands` stays lazily
  created per-frame, as today.)
- `OnDestroy`: `PendingHandle.Complete()` → dispose `Commands` if created → dispose `EventQueue`.
- `OnUpdate`: read `EventQueue` from singleton; `ProducerHandle.Complete()` on the singleton
  copy; after building `Commands` + scheduling the expansion job, write
  `Commands`/`PendingHandle` back into the singleton via `GetSingletonRW`.
- The `DynamicBuffer<ProjectileSpawnEvent>` scope drain is **unchanged**.
- Remove the `internal EventQueue/ProjectileCommands/ProducerHandle/PendingHandle` fields.

**Flavor-A producers** (write `EventQueue`):
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs) (`ProjectileEventWriter`)
- [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs) (`ProjectileEventWriter`)
- [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs) (`ProjectileEventWriter`)
- [StatusProcessSystem.cs](../../Assets/Scripts/System/Status/StatusProcessSystem.cs) (`projectileExpansion.EventQueue`)
- [TimedSpawnSystem.cs](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs) (`projectileExpansion.EventQueue`)

Each: `TryGetSingletonRW<ProjectileSpawnEventSingleton>` → writer from `ValueRO.EventQueue` →
post-schedule combine into `ValueRW.ProducerHandle`.

**Flavor-B consumer** — [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs):
- Replace `GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` +
  `expansionSys.PendingHandle.Complete()` + `expansionSys.ProjectileCommands` with
  `TryGetSingleton<ProjectileSpawnEventSingleton>` → `singleton.PendingHandle.Complete()` →
  read `singleton.Commands`. Preserve the empty/absent no-op path.

## Acceptance criteria

- No `GetExistingSystemManaged<ProjectileSpawnExpansionSystem>()` remains.
- Expansion system exposes no `internal` lane fields.
- Projectiles spawn, fan out, and follow-up/timed spawns still fire.
- `ProjectileSpawnPipelineTests`, `ProjectileCollisionSimulationTests`, and cross-frame
  handoff assertions green.
- Determinism preserved: projectile IDs / fan-out identical (spot-check a seeded scenario).
- Jobs Debugger + Leak Detection clean; no per-frame `Commands` leak across the relocation.

## Scope estimate

Medium. Introduces the A+B combined singleton; template for 004/005.

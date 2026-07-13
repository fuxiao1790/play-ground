# 004 — Producers pack VfxEvent into the single queue

## Goal
Repoint every emit site from `VfxPendingSpawn` to a packed `VfxEvent`. Each producer keeps its
existing singleton-grab + `AsParallelWriter()` + `ProducerHandle`-combine shape; only the writer
element type (`NativeQueue<VfxEvent>`) and the enqueued value change.

## Pattern (every site)
```csharp
// before: vfxQueue = vfx.ValueRO.PendingSpawns;  writer type NativeQueue<VfxPendingSpawn>
// after:  vfxQueue = vfx.ValueRO.Events;         writer type NativeQueue<VfxEvent>
writer.Enqueue(VfxEvent.Pack(typeId, VfxTrigger.Hit, VfxDataType.Point, new VfxPointPayload { Position = pos }));
```
All raw trigger literals (`0..4`) are replaced by `VfxTrigger` members. Keep `HasVfxWriter`/`hasVfx`
guards and the `ProducerHandle = CombineDependencies(...)` lines as-is.

## Sites → payload

**Point (projectiles)** — drop the old `AreaSize` arg:
- `Projectiles/ProjectileCollisionSystem.cs` — `VfxTrigger.Hit` `~355`; `Deactivate` → `CombatDeathUtility` (`Expire`).
- `Projectiles/ProjectileSpawnExpansionSystem.cs` — `VfxTrigger.Spawn` `~288`.
- `Lifetime/CombatLifetimeSystem.cs` `ProjectileLifetimeJob` — `Expire` via `CombatDeathUtility`.

**Area (AOEs)**:
- `Aoes/AoeSpawnExpansionSystem.cs` — `VfxTrigger.Spawn` + `VfxTrigger.Arming` telegraph; **both** IJob blocks (`~101/111`, `~526`). These are plain `IJob`s: keep `ProducerHandle` threaded as input.
- `Aoes/AoeCollisionCore.cs` — `VfxTrigger.Hit` `~233` + `Deactivate`/`Expire` (shared by Impact + Lingering).
- `Lifetime/CombatArmingSystem.cs` `AoeArmingJob` — arm-complete `VfxTrigger.Spawn` `~109`.
- `Lifetime/CombatLifetimeSystem.cs` `AoeLifetimeJob` — `VfxTrigger.Expire`.

**AreaTimed** — task 005 (pulse only).

## CombatDeathUtility.cs
Writer type is now uniform (`NativeQueue<VfxEvent>.ParallelWriter`) for all callers, so **no
projectile/AOE writer split is needed** — only the packed payload differs:
- Projectile `Kill` overloads: pack `VfxPointPayload` (drop the `areaSize` param).
- AOE `Kill` overloads: pack `VfxAreaPayload` (keep `areaSize`).
Collapse the current `EnqueueExpireVfx` into a `point`/`area` pair (or one generic taking a
prebuilt `VfxEvent`).

## Notes / constraints
- No behavior change to which trigger fires where; only the payload representation changes.
- Projectile expire previously passed `max(VisualScale.x, VisualScale.y)` as area — now unused
  by `Point`; drop it.
- Pack is stack-only + `Enqueue` → no hot-path allocation.

## Acceptance
- No remaining `VfxPendingSpawn` references; all producers compile and enqueue `VfxEvent`.
- Job safety unchanged: the two `AoeSpawnExpansionSystem` IJobs still take `ProducerHandle` as input.

## Depends on
- 001, 002, 003.

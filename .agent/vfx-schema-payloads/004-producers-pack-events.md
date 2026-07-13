# 004 — Producers: look up the authored data type, switch, enqueue

## Goal
Each producer reads the data it can, looks up the effect's authored `VfxDataType` from
`DataTypeByKey`, and `switch`es to build the matching payload struct and enqueue it into that
type's queue. No projectile/AOE branch — the switch is on `VfxDataType`, not the source.

## Per-site plumbing (replaces the old single writer)
Each producer grabs the singleton and passes into its job(s): the three `ParallelWriter`s, the
read-only `DataTypeByKey` array, and `HasVfxWriter`. Keep the existing
`ProducerHandle = CombineDependencies(...)` accumulation (covers all three queues).

## Emit helper (shared, Burst)
Add one static helper so every site is identical:
```csharp
static void Emit(
    in VfxWriters w, in NativeArray<VfxDataType> byKey,   // w = the three ParallelWriters
    int typeId, VfxTrigger trigger,
    float2 position, float areaSize, float duration, float tickRate)
{
    int i = VfxKey.FlatIndex(typeId, trigger);
    VfxDataType dt = (uint)i < (uint)byKey.Length ? byKey[i] : VfxDataType.None;
    var key = new VfxGraphKey(typeId, trigger);
    switch (dt)
    {
        case VfxDataType.Point:     w.Point.Enqueue(new VfxPointPayload { Key = key, Position = position }); break;
        case VfxDataType.Area:      w.Area.Enqueue(new VfxAreaPayload { Key = key, Position = position, AreaSize = areaSize }); break;
        case VfxDataType.AreaTimed: w.Timed.Enqueue(new VfxAreaTimedPayload { Key = key, Position = position, AreaSize = areaSize, Duration = duration, TickRate = tickRate }); break;
        // None → no effect registered → skip.
    }
}
```
Callers pass whatever source fields they have; unused ones are ignored by the chosen case.

## Sites (each calls `Emit` with the trigger + available fields)
- `Projectiles/ProjectileCollisionSystem.cs` — `Hit` `~355`; `Deactivate` → `CombatDeathUtility` (`Expire`).
- `Projectiles/ProjectileSpawnExpansionSystem.cs` — `Spawn` `~288`.
- `Aoes/AoeSpawnExpansionSystem.cs` — `Spawn` + `Arming`; **both** IJob blocks (`~101/111`, `~526`). Plain `IJob`s: keep `ProducerHandle` threaded as input.
- `Aoes/AoeCollisionCore.cs` — `Hit` `~233` + `Deactivate`/`Expire` (Impact + Lingering).
- `Lifetime/CombatArmingSystem.cs` `AoeArmingJob` — arm-complete `Spawn` `~109`.
- `Lifetime/CombatLifetimeSystem.cs` `ProjectileLifetimeJob` (`Expire`) + `AoeLifetimeJob` (`Expire`).
- **No `Pulse` trigger exists** — the lingering pulse rides on the `Spawn` event's `Duration`/`TickRate`
  (task 006); the two Spawn sites above (`AoeSpawnExpansionSystem`, arm-complete `AoeArmingJob`) pass
  those fields, everything else passes `0`.

## Field sources
`position` from `CombatKinematicsComponent`; `areaSize` from `AoeAreaComponent.Size` (AOE) or
`max(CombatRenderAuthoring.VisualScale.xy)` (projectile, already used today); `duration` from
`CombatLifetimeComponent.Remaining`; `tickRate` from `AoeHitGateComponent.RepeatHitCooldownSeconds`
/ `AoePulseVfxComponent.Interval`. Pass `0` for fields a site doesn't read.

## ECS field-availability (verified — no ComponentLookup needed for the current type set)
A job only ever builds the types authored on the `(typeId,trigger)` keys it emits, and each field
that type needs must exist on every entity that job iterates. Checked against the actual queries:
- `Point` needs only `position` (all entities).
- `Area` needs `position` + `AoeAreaComponent.Size` — every AOE carries `AoeAreaComponent`; the two
  collision jobs are already archetype-split (`ImpactAoeCollisionJob [WithNone(LingeringAoeTag)]`;
  `LingeringAoeCollisionJob` requires `ref AoeHitGateComponent` → lingering-only).
- `AreaTimed` is authored ONLY on a lingering AOE's **Spawn** effect (task 006 folds the old pulse
  into Spawn). So only the two Spawn emit sites supply timing: the non-arming site reads
  Duration/TickRate from the `AoeSpawnCommand` (`IJob` over commands → no ECS spanning); the
  arm-complete site (`AoeArmingJob`, unchanged/unsplit) reads them **optionally** via
  `ComponentLookup` (impact lacks the components → 0, and its Spawn type is `Area` anyway).

So **every non-Spawn emit site passes `duration = tickRate = 0` and reads no timing component**.
No job splitting; `ComponentLookup` is used only at the arm-complete Spawn (task 006).

> Rule: registration's exact-match validation guarantees a graph's *buffer* exists but NOT that the
> producer supplies non-zero *data*. A trigger authored `AreaTimed` must be emitted from a site that
> can read its timing fields (that is why the lingering timing lives on the Spawn emit).

## CombatDeathUtility.cs
Collapse the projectile/AOE overloads into one `Kill(... in VfxWriters, in NativeArray<VfxDataType>,
hasVfxWriter, int typeId, float2 pos, float area, float duration)` that calls `Emit` for the
`Expire` trigger — same code path for both callers.

## Notes / constraints
- No hot-path allocation: `Emit` is stack-only + `Enqueue`; the lookup is one array read.
- Job safety unchanged: writers grabbed from the singleton do NOT auto-chain — keep threading the
  owner's `ProducerHandle` (incl. the two `AoeSpawnExpansionSystem` IJobs), per
  `reference_shared_queue_producer_chaining`.

## Acceptance
- No `VfxPendingSpawn` references; every emit site routes through `Emit`, switching on the
  looked-up `VfxDataType`; no producer branches on projectile-vs-AOE.

## Depends on
- 001, 002, 003.

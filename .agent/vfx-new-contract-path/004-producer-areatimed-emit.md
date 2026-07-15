# 004 — Producer emits Area-Timed at lingering-AOE spawn

## Goal
Make a producer actually emit the new contract, chosen from authored data via the flat lookup, with
the existing Area emit kept as the default branch.

## Primary site: `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs` (~line 95-119)
The non-arming spawn emit currently always enqueues an Area `AoeVfxSpawnRequest`
(`AoeSpawnExpansionSystem.cs:111`). Change to branch on the authored data type:
- Job gains RO access to `DataTypeByKey` (pass the `NativeList<VfxDataType>` as a job field; read
  `dt = FlatIndex(command.TypeId, Spawn) < len ? DataTypeByKey[idx] : Area`).
- Job also gains the Area-Timed queue's `.ParallelWriter` + `hasTimedWriter` flag (same wiring
  pattern as the existing `vfxPending` writer; combine job handle into the shared `ProducerHandle`).
- Branch:
  - `dt == AreaTimed` -> enqueue `VfxAreaTimedRequest { TypeId, Trigger = Spawn, Position = pos,
    AreaSize = command.AreaSize, DurationMs = command.Lifetime * 1000f,
    TickIntervalMs = command.RepeatHitCooldownSeconds * 1000f }` into the timed queue.
  - else -> existing Area emit unchanged.
- Leave the `ArmSeconds > 0` telegraph (`Arming` trigger) branch exactly as is — arming telegraph is
  always the Area contract.

## System wiring: `AoeSpawnExpansionSystem` OnUpdate
- Read the singleton's `PendingAreaTimed` + `DataTypeByKey`, build the parallel writer, pass both
  into the job, and combine the scheduled handle into `singleton.ProducerHandle` (mirror the
  existing `vfxWriter`/`ProducerHandle` handling used by `AoePulseVfxSystem.cs:40-44`).

## Secondary site (sub-step, may ship after): `Assets/Scripts/System/Lifetime/CombatArmingSystem.cs:109`
Arm-complete spawn emit. Same branch, but `Duration`/`TickInterval` are not on the command here.
Read them via optional `ComponentLookup`: `CombatLifetimeComponent.Remaining` (lingering duration)
and `AoeHitGateComponent.RepeatHitCooldownSeconds`. Mark clearly; not required for the first slice.

## Explicitly NOT changed
- `AoePulseVfxSystem` stays (existing per-tick Pulse path is not migrated).
- `CombatDeathUtility` (Expire) and `AoeCollisionCore` (Hit) keep Area emit — those triggers are not
  Area-Timed.

## Acceptance criteria
- A lingering AOE whose Spawn effect is authored Area-Timed emits one `VfxAreaTimedRequest` with
  correct ms-converted Duration/TickInterval; an Area-authored (or unauthored) AOE emits the same
  Area request as before.
- No new component added to any entity; contract picked purely from `DataTypeByKey`.
- Job dependencies flow through the shared `ProducerHandle`; no safety errors under `ScheduleParallel`.

## Dependencies
- 001 (lookup), 002 (queue/struct), 005 (authoring must set the data type for the branch to fire).

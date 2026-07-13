# 006 — Fold per-tick pulse into the Spawn event (Duration + TickRate)

## Goal (user directive)
Stop emitting a pulse VFX event **per tick**. For a lingering AOE, its **Spawn** event carries
`Duration` (active lifetime) + `TickRate` (tick interval); the graph self-pulses on the GPU for
that duration. Since lingering AOEs are stationary, one spawn event with duration+tickRate is
equivalent to one event per tick. There is **no separate `Pulse` trigger** and **arming is
unchanged**.

## Consequences
- `VfxTrigger.Pulse` is removed (task 001). A lingering AOE authors its **Spawn** effect as
  `VfxDataType.AreaTimed`; impact AOEs author Spawn as `Area` (or `Point`). Authoring is task 005.
- **Arming stays exactly as today** — the `Arming` telegraph is emitted when the arming entity
  enters the world; the spawn burst stays deferred to arm-complete. No arming split.

## Delete (per-tick pulse is fully replaced by the Spawn event)
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs` — the whole system.
- `AoePulseVfxComponent` in `AoeEcsComponents.cs` — redundant: `RemainingInterval` (tick countdown)
  is dead; `Interval` only duplicated `AoeHitGateComponent.RepeatHitCooldownSeconds` (see `PulseVfxFor`).
- `AoeSpawnApplySystem.cs` — `PulseVfxFor` + the `AoePulseVfxComponent` archetype type (`~260`),
  chunk handle (`~334/377`), and write (`~413–450`).

## Spawn event carries Duration + TickRate (task-004 `Emit`, trigger `Spawn`)
The Spawn emit already exists at both go-live sites; it now also passes `duration`/`tickRate`.
`DataTypeByKey[(typeId, Spawn)]` decides the payload (`AreaTimed` for lingering, `Area`/`Point`
otherwise), so the same call works everywhere:
- **Non-arming** — `Aoes/AoeSpawnExpansionSystem.cs` shared `AoeExpansionCore.Expand` (already emits
  Spawn). The `AoeSpawnCommand` carries `Lifetime` (→ `duration`) and `RepeatHitCooldownSeconds`
  (→ `tickRate`); pass them. It's an `IJob` over command arrays → no ECS-component spanning; impact
  commands just resolve to `Area`/`Point` and ignore the timing fields.
- **Arm-complete** — `Lifetime/CombatArmingSystem.cs` `AoeArmingJob` (unchanged structure) emits the
  deferred Spawn. To supply the lingering timing without changing the query or splitting the job,
  read them **optionally** via `ComponentLookup` (add `Entity entity` to `Execute`):
  `duration = LifetimeLookup.TryGetComponent(entity, out var lt) ? lt.Remaining : 0f`,
  `tickRate = HitGateLookup.TryGetComponent(entity, out var hg) ? hg.RepeatHitCooldownSeconds : 0f`.
  Impact entities lack both → 0, and their `Spawn` type is `Area` anyway. `[ReadOnly]`
  `ComponentLookup<CombatLifetimeComponent>` + `ComponentLookup<AoeHitGateComponent>`; the scheduler
  serializes vs `CombatLifetimeSystem`'s writes (ordering only — different entities, no conflict).

## Tests
- Remove `AddSystemToUpdateList(...AoePulseVfxSystem)` — `AoeSimulationTests.cs:64`,
  `ProjectileCollisionSimulationTests.cs:60`.
- Remove `AoePulseVfxComponent` assertions/usage — `AoeSimulationTests.cs:599, 602, 1664`.

## Notes / constraints
- Behavior change is intentional: pulsing is now a single spawn-time event the graph replays on the
  GPU (valid because lingering AOEs don't move).
- Only the arm-complete Spawn uses `ComponentLookup`; every other emit site reads no timing component.

## Acceptance
- `AoePulseVfxSystem` + `AoePulseVfxComponent` + `VfxTrigger.Pulse` gone; project + tests compile.
- A lingering AOE emits ONE `Spawn` (`AreaTimed`) event with non-zero `Duration`/`TickRate` and the
  graph pulses for its whole life; impact AOEs emit a plain `Area`/`Point` Spawn. Arming behavior
  is byte-for-byte the same as today except the spawn payload gained timing for lingering.

## Depends on
- 001–005.

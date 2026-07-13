# 005 — DataType registration + AreaTimed pulse wiring

## Goal
Register each graph with its `VfxDataType`, and make the lingering-AOE pulse emit the new
`AreaTimed` payload (Duration + TickRate), proving the framework carries a graph-specific
buffer set no other effect uses.

## Files
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs`

## SkillDriver registration (replaces `requireAreaSizeContract`, uses `VfxTrigger`)
- `RegisterProjectileVfx` — `Register(typeId, VfxTrigger.Spawn/Hit/Expire/Arming, asset, VfxDataType.Point)`.
- `RegisterAoeVfx` — `Spawn/Hit/Expire/Arming` → `VfxDataType.Area`; **`VfxTrigger.Pulse`** → `VfxDataType.AreaTimed`.

A single AOE's pulse graph `(typeId, VfxTrigger.Pulse)` registers `AreaTimed` while its other
triggers register `Area` — fine, registration is per `(typeId, trigger)`.

## AoePulseVfxSystem
- `AoePulseVfxJob.Execute` already has `AoePulseVfxComponent` (→ `TickRate = pulseVfx.Interval`).
  Add `in CombatLifetimeComponent lifetime` to source `Duration = lifetime.Remaining`.
  - The job matches only entities with `AoePulseVfxComponent` (lingering AOEs), which also carry
    `CombatLifetimeComponent` (ecs-notes: lifetime is plain timer data on lingering AOEs), so the
    added `in` component does not wrongly narrow the query. **Verify** in PlayMode.
- Emit:
  ```csharp
  VfxPending.Enqueue(VfxEvent.Pack(identity.TypeId, VfxTrigger.Pulse, VfxDataType.AreaTimed,
      new VfxAreaTimedPayload {
          Position = kinematics.Position, AreaSize = area.Size,
          Duration = lifetime.Remaining, TickRate = pulseVfx.Interval }));
  ```

## Acceptance
- Projectile graphs register as `Point` (no `AreaSizes` buffer); AOE non-pulse as `Area`; pulse
  as `AreaTimed`.
- Pulse graphs receive populated `Durations`/`TickRates` buffers.

## Depends on
- 001–004.

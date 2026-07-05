# 003 — AoeDelaySystem (tick + activate)

## Goal
New system that ticks `AoeDelayComponent.Remaining` on windup AOEs and, on expiry,
flips them into their live state. Mirrors `CombatLifetimeSystem`'s shape (one
system, two burst `IJobEntity`s, `ScheduleParallel`, per-entity `EnabledRefRW`).
**Not a VFX producer** (telegraph is emitted at spawn in 004; no activation VFX).

## File
`Assets/Scripts/System/Aoe/AoeDelaySystem.cs`

```
[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(CombatLifetimeSystem))]   // activate before lifetime ticks / collision runs
public partial struct AoeDelaySystem : ISystem
```
Two jobs scheduled sequentially on `state.Dependency` (they touch disjoint
archetypes, but chaining is simplest and cheap):

### ImpactAoeDelayJob
- `[WithAll(AoeTag, Active, AoeDelayComponent)] [WithNone(CombatLifetimeComponent)]`
- Execute reads `ref AoeDelayComponent delay`, `EnabledRefRW<AoeCollisionActiveTag>`,
  `EnabledRefRW<Active>`, `EnabledRefRW<CombatRenderActiveTag>`,
  `EnabledRefRW<AoeDelayComponent>`.
- `delay.Remaining -= dt`. If `> 0` return.
- On expiry: `delayEnabled.ValueRW = false`. If `delay.ActivateCollision == 1`:
  `collisionActive.ValueRW = true` (Active + render already true → impact collision
  one-shots this frame, then deactivates itself). Else (visual-only telegraph):
  `active.ValueRW = false; renderActive.ValueRW = false` (entity returns to pool).

### LingeringAoeDelayJob
- `[WithAll(AoeTag, Active, AoeDelayComponent)] [WithPresent(CombatLifetimeComponent)]`
  (`WithPresent` because lifetime is **disabled** during windup).
- Execute reads `ref AoeDelayComponent delay`,
  `EnabledRefRW<CombatLifetimeComponent>`, `EnabledRefRW<AoeCollisionActiveTag>`,
  `EnabledRefRW<TimedSpawnComponent>`, `EnabledRefRW<AoeDelayComponent>`.
- `delay.Remaining -= dt`. If `> 0` return.
- On expiry: `delayEnabled.ValueRW = false`; `lifetimeEnabled.ValueRW = true`
  (begins ticking from the full `Remaining` staged in 002); if
  `delay.ActivateCollision == 1` → `collisionActive.ValueRW = true`; if
  `delay.ActivateTimedSpawn == 1` → `timedSpawnEnabled.ValueRW = true`.
  (Render + `Active` already true.)

## Ordering rationale
`AoeDelaySystem` created late-frame in apply means a freshly spawned windup AOE is
first ticked next frame → full delay honored. Running `UpdateBefore
CombatLifetimeSystem` (which itself is before `TimedSpawnSystem` and the
collision/contact systems) makes activation take effect the same frame it fires.

## Acceptance criteria
- Impact windup AOE deals no hits until `Remaining` reaches 0, then deals its single
  pass and deactivates.
- Lingering windup AOE does not tick lifetime / spawn interval children until
  activation; after activation it lives its full authored `lifetimeSeconds`.
- Visual-only (`ActivateCollision == 0`) impact windup AOE deactivates on expiry
  (no stuck-alive slot); pool reuse still works.
- No safety errors from parallel enableable writes (pattern matches
  `CombatLifetimeSystem`).

## Dependencies
001, 002.

## Scope
Medium.

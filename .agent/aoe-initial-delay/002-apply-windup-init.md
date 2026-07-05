# 002 — Apply: initialize windup vs immediate state

## Goal
When `cmd.InitialDelaySeconds > 0`, the apply systems must materialize the AOE in
the **inert windup** state; otherwise they behave exactly as today. Covers both
the parallel reuse job and the cold-create record path, for impact and lingering.

## Windup vs immediate initial state
Let `windup = cmd.InitialDelaySeconds > 0f`.

**Impact** (`ImpactAoeSpawnJob` + `RecordImpactReset`):
- immediate (today): `Active = collisionEnabled`, `AoeCollisionActiveTag =
  collisionEnabled`, `CombatRenderActiveTag = collisionEnabled`, `AoeDelayComponent
  disabled`.
- windup: `Active = true`, `AoeCollisionActiveTag = false`,
  `CombatRenderActiveTag = true` (sprite shows), `AoeDelayComponent enabled` with
  `Remaining = InitialDelaySeconds`, `ActivateCollision = collisionEnabled ? 1 : 0`,
  `ActivateTimedSpawn = 0`.

**Lingering** (`LingeringAoeSpawnJob` + `RecordLingeringReset`):
- immediate (today): lifetime enabled `Remaining = Lifetime`; collision =
  `collisionEnabled`; timed spawn enabled iff `hasTimedSpawner`; render/Active on;
  `AoeDelayComponent disabled`.
- windup: `CombatLifetimeComponent` written `Remaining = Lifetime` but **disabled**;
  `AoeCollisionActiveTag = false`; `TimedSpawnComponent` written but **disabled**
  (state still initialized via `InitialTimedSpawnStateFor` so activation just
  re-enables); `CombatRenderActiveTag = true`; `Active = true`;
  `AoeDelayComponent enabled` with `Remaining = InitialDelaySeconds`,
  `ActivateCollision = collisionEnabled ? 1 : 0`,
  `ActivateTimedSpawn = hasTimedSpawner ? 1 : 0`.

Note: on the non-windup path `AoeDelayComponent` must be explicitly **disabled**
(new enableable components default to enabled on `CreateEntity`).

## Changes
### `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
Add to `AoeSpawnApplyUtility`:
```csharp
public static bool HasWindup(in AoeSpawnCommand cmd) => cmd.InitialDelaySeconds > 0f;

public static AoeDelayComponent DelayFor(in AoeSpawnCommand cmd) => new()
{
    Remaining = cmd.InitialDelaySeconds,
    ActivateCollision = (byte)(NeedsCollision(cmd) ? 1 : 0),
    ActivateTimedSpawn = (byte)(HasTimedSpawner(cmd) ? 1 : 0),
};
```

- **`ImpactAoeSpawnJob`**: add `ComponentTypeHandle<AoeDelayComponent> DelayHandle`
  (RW) + `chunk.GetEnabledMask(ref DelayHandle)` + `GetNativeArray`. After
  `WriteCommon`, branch on `HasWindup(cfg)` to set the masks/values per the table
  above. Wire `DelayHandle = GetComponentTypeHandle<AoeDelayComponent>(false)` in
  the scheduling block.
- **`RecordImpactReset`**: set `AoeDelayComponent` value + `SetComponentEnabled`
  per table; adjust the `Active` / `AoeCollisionActiveTag` / `CombatRenderActiveTag`
  enables for the windup branch.
- **`LingeringAoeSpawnJob`**: add `AoeDelayComponent` handle + mask/array; branch on
  `HasWindup(cfg)`: windup disables `lifetimeMask[i]`, `collisionActiveMask[i]`,
  `timedSpawnMask[i]`, enables `delayMask[i]`, `renderActiveMask[i] = true`,
  `activeMask[i] = true`. Keep writing `lifetimes[i]`, `timedSpawns[i]`,
  `timedSpawnStates[i]`, `pulseVfxs[i]`, `gates[i].Clear()` as today (values staged,
  just disabled).
- **`RecordLingeringReset`**: same windup branch via `SetComponentEnabled`.

## Acceptance criteria
- `delay <= 0`: identical component/enable state to current `main` (diff-verify the
  immediate branch is unchanged).
- `delay > 0` impact: entity `Active` + rendered, collision disabled, delay enabled.
- `delay > 0` lingering: `Active` + rendered, collision + lifetime + timed spawn all
  disabled, delay enabled; `CombatLifetimeComponent.Remaining == Lifetime`.
- Reused pool slots reset cleanly into whichever state the new command dictates
  (windup ↔ immediate across reuses).

## Dependencies
001.

## Scope
Medium (four write paths, mirror carefully).

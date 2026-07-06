# 003 - Spawn Apply Derives Lifecycle State

## Goal

Make projectile and AOE spawn apply the only authority for initial lifecycle
phase and derived enable gates.

Commands may carry lifecycle inputs:

```text
ArmingSeconds
Lifetime / armed duration input
```

Commands must not carry lifecycle state:

```text
Phase
Active enabled
sprite enabled
collision enabled
timed-spawn enabled
```

## Scope

- Add plain lifecycle input fields to `ProjectileSpawnCommand` and
  `AoeSpawnCommand` as needed.
- Keep events slim; events still carry only template key plus instance frame.
- Normalize template hashing so per-instance fields remain zeroed.
- Introduce shared helper shape, for example:

  ```text
  CombatLifecycleSpawnState
  ```

  It derives:

  - initial phase
  - `Active`
  - sprite gate
  - domain collision gate
  - timed-spawn gate
  - initial lifetime remaining
  - initial arming remaining

- Use domain-specific adapters for collision gates because projectile and AOE
  collision tags differ.
- Route projectile reuse/cold-create and AOE reuse/cold-create through these
  helpers.
- Retire or replace the AOE-only `SpawnStateFor` helper.

## Acceptance Criteria

- Projectile apply has one spawn-state derivation point.
- AOE apply has one spawn-state derivation point shared in concept with
  projectile apply.
- Current zero-arming behavior matches existing projectile, impact AOE, and
  lingering AOE behavior.
- External spawners still pass data, not state.

## Dependencies

001 and 002.

## Estimated Scope

High. Touches both spawn apply systems and command/template hashing.

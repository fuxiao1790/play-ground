# 004 Query And Simulation Updates

## Goal

Update all simulation systems that currently interpret component presence as behavior.

## Scope

- `TimedSpawnSystem`
  - Replace `[WithAll(... TimedSpawnTag)]` with enabled `TimedSpawnComponent`.
  - Keep `Active` and enabled `CombatLifetimeComponent` filtering.
  - Continue to guard against `CombatFaction.None`.
- `ImpactAoeCollisionSystem`
  - Replace `WithNone<CombatLifetimeComponent>` with disabled lifetime selection.
  - Keep impact behavior as one-shot collision that deactivates after pass.
- `LingeringAoeCollisionSystem`
  - Keep lifetime-present access but process enabled and disabled lifetime according to intended pulse/lingering behavior.
  - Ensure it does not double-process impact AOEs if impact system owns disabled-lifetime AOEs. If one collision system handles both, remove duplicate impact path.
- `AoePulseVfxSystem`
  - Continue to skip when lifetime is disabled or pulse interval is inert.
- `CombatLifetimeSystem`
  - Confirm enabled `CombatLifetimeComponent` query skips impact AOEs and non-lifetime slots.
- Projectile tracking/collision
  - Confirm all projectiles still have enabled lifetime on spawn.

## Acceptance Criteria

- No simulation behavior depends on `HasComponent<TimedSpawnTag>()`.
- No AOE behavior depends on missing `CombatLifetimeComponent`.
- Impact AOEs collide once.
- Lingering AOEs tick lifetime, repeat gates, pulse VFX, and expire.
- Timed spawners emit children only when `TimedSpawnComponent` is enabled.

## Dependencies

Depends on 001, 002, and 003.

## Complexity

Medium.


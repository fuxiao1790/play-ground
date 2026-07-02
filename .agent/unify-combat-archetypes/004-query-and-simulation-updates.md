# 004 Query And Simulation Updates

## Goal

Update the one simulation system that interprets `TimedSpawnTag` as behavior, and
confirm the AOE collision/lifetime/pulse systems need no change under the
three-archetype design.

## Scope

- `TimedSpawnSystem` (the only behavioral change)
  - Replace `[WithAll(Active, CombatLifetimeComponent, TimedSpawnTag)]` with
    `[WithAll(Active, CombatLifetimeComponent)]` plus enabled `TimedSpawnComponent`.
  - For enableable components, `WithAll` already means present + enabled, so the
    job is scheduled only for active, enabled-lifetime, enabled-timed-spawn
    entities. Do not schedule all lifetime entities and branch on
    `TimedSpawnComponent` enabled state inside `Execute`.
  - Continue to guard against `CombatFaction.None`.
  - Runs for both projectiles and lingering AOEs; impact AOEs lack lifetime and
    timed-spawn and are excluded by construction.

- `ImpactAoeCollisionSystem` — no change.
  - Impact AOEs stay lifetime-absent, so `WithNone<CombatLifetimeComponent>`
    still selects exactly impact AOEs. Keep one-shot deactivate-after-pass.

- `LingeringAoeCollisionSystem` — no change.
  - Lingering AOEs keep present lifetime, so `WithPresent<CombatLifetimeComponent>`
    still selects exactly lingering AOEs (both timed and non-timed; timed-spawn
    enabled state is irrelevant to collision). No impact AOE matches.

- `CombatLifetimeSystem` — confirm, no change.
  - `[WithAll(AoeTag, Active, CombatLifetimeComponent)]` matches present+enabled
    lifetime; impact AOEs are excluded by absence. Same for the projectile job.

- `AoePulseVfxSystem` — confirm, no change.
  - Already reads `EnabledRefRO<CombatLifetimeComponent>` and skips disabled;
    only lingering AOEs carry the component.

- `AoeContactGateSystem` — confirm it only touches lingering AOEs (contact-gate
  buffer is present only on the lingering archetype).

- Projectile tracking/collision — confirm all projectiles still spawn with
  enabled lifetime and that the added timed-spawn components do not affect their
  queries.

## Acceptance Criteria

- No simulation behavior depends on `HasComponent<TimedSpawnTag>()`.
- Impact and lingering collision remain disjoint; no AOE is processed by both.
- Impact AOEs collide once; lingering AOEs tick lifetime, repeat gates, pulse VFX,
  and expire.
- Timed spawners emit children only when `TimedSpawnComponent` is enabled (and
  therefore lifetime is enabled).
- No new per-entity `if` check is introduced as the primary category selector.

## Dependencies

Depends on 001, 002, and 003.

## Complexity

Small.

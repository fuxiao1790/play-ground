# 007 - Regression Tests

## Goal

Guard current behavior and new lifecycle timing before enabling authored arming
content broadly.

## Test Coverage

- Current no-arming projectile spawns as:
  - `Active` enabled
  - phase `Armed`
  - sprite gate enabled
  - collision gate derived from hit payload
  - timed spawn enabled only when configured
- Current no-arming impact AOE behavior is preserved, including visual-only or
  inert cases.
- Current no-arming lingering AOE behavior is preserved.
- Arming projectile/AOE:
  - `Active` enabled
  - sprite gate disabled
  - collision disabled
  - timed spawn disabled
  - arming VFX requested
- `Arming -> Armed` transition:
  - enables sprite
  - enables collision only if payload needs collision
  - enables timed spawn only if configured
  - stops arming VFX state/request
- Large delta:
  - leftover armed delta is exposed in same frame
  - timed spawn can emit catch-up ticks
  - pulse VFX can emit catch-up ticks
  - catch-up caps are respected
- Death:
  - disables `Active`
  - disables sprite, collision, timed spawn, arming VFX
  - disabled slots are reusable through `WithDisabled<Active>()`

## Acceptance Criteria

- Tests fail on any gate mismatch in reuse or cold-create paths.
- Tests cover projectile, impact AOE, and lingering AOE.
- Tests cover both normal dt and large dt.

## Dependencies

001 through 006 as relevant. Some characterization tests can be written before
implementation.

## Estimated Scope

High. Broad lifecycle refactor needs broad tests.

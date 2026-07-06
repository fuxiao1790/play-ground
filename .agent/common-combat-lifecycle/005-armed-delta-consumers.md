# 005 - Armed Delta Consumers

## Goal

Make armed-only timer systems consume `CombatArmedDeltaComponent.Value` instead
of raw `SystemAPI.Time.DeltaTime`.

## Scope

- Update `TimedSpawnSystem`:
  - query `CombatArmedDeltaComponent`
  - skip when armed delta is `<= 0`
  - subtract armed delta from cooldown
  - preserve positive-min interval guard
  - preserve max catch-up ticks per update
- Update `AoePulseVfxSystem`:
  - consume armed delta
  - use bounded catch-up if more than one pulse interval elapsed
  - skip while arming
- Audit any other armed-only systems that currently use raw delta.

## Acceptance Criteria

- Large frame crossing `Arming -> Armed` can emit timed children/pulse VFX using
  leftover armed time in the same frame.
- Systems do not tick during `Arming`.
- Catch-up remains capped.
- Current zero-arming behavior is unchanged.

## Dependencies

004.

## Estimated Scope

Medium.

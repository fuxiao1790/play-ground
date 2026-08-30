---
name: rewrite-cadence-tests
description: Delete the three cooldown-premised lingering AOE tests and add one covering every-tick collision; flag chunk-capacity test for a user re-run.
---

# 005 - Rewrite Cadence Tests

## Depends On

[001](001-remove-gate-from-lingering-collision.md) through
[003](003-delete-hit-gate-component.md) — write this against the new
behavior, not before it exists.

## Changes

### [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)

Delete these three tests — their entire premise is the removed cooldown gate,
and none of them have a meaningful rewritten form distinct from a plain
"hits every tick" test:

- `LingeringTargetIsNotRehitUntilTickIntervalExpires` ([lines 477-491](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L477-L491)) — asserts a *miss* on the second tick; under the new behavior this tick would hit, so the assertion is simply wrong now, not adaptable.
- `LingeringHitsImmediatelyThenRepeatsAfterCooldown` ([lines 493-507](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L493-L507)) — asserts exactly 2 hits across 3 ticks; superseded by the new test below.
- `LingeringReentryWaitsForNextTickInterval` ([lines 509-528](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L509-L528)) — uses `tickInterval: 100f` specifically to hold the gate shut across a target's exit/re-entry; that mechanism no longer exists.

Add one new test in their place, e.g. `LingeringHitsEveryTickWhileTargetPresent`:
- `AddTarget(float2.zero, 0.25f, 1)`; `SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 0.05f)` (the `tickInterval` argument is now VFX-only per task 004, but keep passing a representative value so the test still exercises normal authoring shape).
- `Tick(0.01f)` three times in a row.
- Assert `ReadHitCount() == 1` after each individual tick (i.e. the target is hit once per tick, every tick, with no gap) — this directly encodes the new "no throttling" contract.

Leave `LingeringExpiresAndDeactivates` ([line 530 onward](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L530))
as-is — it only asserts lifetime expiry/deactivation, not hit cadence, and
still compiles under the renamed field from task 004.

### Flag for user verification (do not attempt to fix blindly)

`ImpactArchetypeOmitsLingeringOnlyComponentsAndHasLargerChunkCapacity`
(~[line 609](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L609)) may
assert an exact chunk-capacity number that shifts once `AoeHitGateComponent`
is removed from both archetypes (smaller entity size → more entities per
chunk). This repo's convention is that the agent does not run tests itself —
flag this test by name for the user to run and confirm/update the expected
capacity value after task 003 lands.

## Acceptance Criteria

- No test in the file references `AoeHitGateComponent`, gate cooldown
  semantics, or asserts a "miss" tick for a stationary target inside a
  lingering AOE's area.
- The new every-tick test passes against the post-001 behavior.
- The chunk-capacity test is explicitly called out to the user, not silently
  left with a possibly-stale expected value.

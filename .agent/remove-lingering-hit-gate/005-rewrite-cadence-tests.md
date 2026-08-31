---
name: rewrite-cadence-tests
description: Delete cooldown-premised lingering AOE tests, add every-tick collision coverage, and retain the relative chunk-capacity invariant.
---

# 005 - Rewrite Cadence Tests

## Depends On

[001](001-remove-gate-from-lingering-collision.md) through
[003](003-delete-hit-gate-component.md) must land first so these tests target
the new behavior.

## Changes

### [AoeSimulationTests.cs](../../Assets/Tests/PlayMode/AoeSimulationTests.cs)

Delete these cooldown-premised tests:

- `LingeringTargetIsNotRehitUntilTickIntervalExpires`
- `LingeringHitsImmediatelyThenRepeatsAfterCooldown`

Add `LingeringHitsEveryTickWhileTargetPresent`:

- `AddTarget(float2.zero, 0.25f, 1)`.
- `SpawnCircle(float2.zero, 1f, 2f, lifetime: 10f, tickInterval: 0.05f)`.
  `tickInterval` is VFX-only after task 004; retaining a representative value
  proves it no longer gates collision.
- Call `Tick(0.01f)` three times.
- Assert `ReadHitCount() == 1` after each tick.

Rewrite `LingeringReentryWaitsForNextTickInterval` as
`LingeringReentryHitsOnNextTick`:

- Hit once while target is inside.
- Move target outside, tick, and assert zero hits.
- Move target inside, tick, and assert one hit immediately on that next
  simulation tick.
- Keep a nonzero representative `tickInterval` to prove VFX cadence does not
  delay collision after re-entry.

Leave `LingeringExpiresAndDeactivates` unchanged; it covers lifetime expiry,
not collision cadence.

Audit other `tickInterval: 100f` uses in this class. They currently occur in
on-hit spawn/chain tests and use the old value as an implicit one-hit gate.
Replace those values with a representative VFX interval such as `0.05f`, keep
their intended registry/chain assertions, and update comments so none implies
that `tickInterval` suppresses later collisions.

### Retain the chunk-capacity invariant

`ImpactArchetypeOmitsLingeringOnlyComponentsAndHasLargerChunkCapacity` asserts
only `impactCapacity > lingeringCapacity`; it contains no exact expected
capacity. Keep this test unchanged. Removing the same component from both
archetypes may change absolute capacities, but there is no expected number to
update. User verification is specified in task 007.

## Acceptance Criteria

- No test references `AoeHitGateComponent` or expects an interval-gated miss.
- New every-tick and immediate-re-entry tests are included in user-run
  verification.
- On-hit spawn/chain tests no longer describe `tickInterval` as a collision
  gate.
- Relative chunk-capacity test remains unchanged and is included in user-run
  verification.

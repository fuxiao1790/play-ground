---
name: delete-hit-gate-component
description: Delete the AoeHitGateComponent type now that nothing references it, and fix up test-helper archetypes.
---

# 003 - Delete the Component Type

## Depends On

[001](001-remove-gate-from-lingering-collision.md) and
[002](002-remove-gate-from-spawn-materialization.md) — must land first so
this is a genuine zero-reference deletion, not a break.

## Changes

### [AoeEcsComponents.cs](../../Assets/Scripts/System/Aoes/AoeEcsComponents.cs)

Delete the `AoeHitGateComponent` struct ([lines 37-42](../../Assets/Scripts/System/Aoes/AoeEcsComponents.cs#L37-L42),
including its `RepeatHitCooldownSeconds`-explaining comment).

### Test fixture archetypes

These build synthetic archetypes matching production shape and currently
list `typeof(AoeHitGateComponent)` — remove that entry from each so their
archetypes match the now-slimmer production ones:
- [CombatPoolCleanupSystemTests.cs:417](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs#L417) (`CreateImpactAoe` helper).
- [AoeSimulationTests.cs:1662](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1662) (`CreateDisabledImpactAoeSlot`).
- [AoeSimulationTests.cs:1688](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1688) (`CreateDisabledLingeringAoeSlot`).

Search the full repo for `AoeHitGateComponent` after this task to confirm
zero remaining references (there is one more, in `Docs/reference/simulation/aoe-system.md`
— handled in task 006, not here).

## Acceptance Criteria

- `AoeHitGateComponent` does not exist anywhere in `Assets/`.
- The three test-helper archetypes above compile and match their
  corresponding production archetype's component set (minus the deleted
  component).
- Full solution builds clean.

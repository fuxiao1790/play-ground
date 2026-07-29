# Task Execution Packet

## Task

005-verify-mana-cost-support-assets.md

## Goal

Perform user-owned Unity-editor verification that serialized support assets and FasterProjectiles behavior remain correct after the interface migration.

## Files Allowed To Modify

- None by the implementation agent.

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Dependencies Confirmed

- Task 001 support interface migration is complete and statically validated.

## User Verification Required

1. In Unity, inspect an existing `PiercingSupport` or `AddedDamageSupport` asset and confirm its own-stat and `manaCostAdded` values remain present and effective.
2. Play a loadout using `FasterProjectilesSupport` and confirm both projectile speed and lifetime scale.
3. Optionally set a new mana percent/multiplier through the Inspector to author a balance tradeoff.

## Hard Boundaries

- Do not hand-edit `.asset` YAML.
- Do not change code or tests as part of this verification.

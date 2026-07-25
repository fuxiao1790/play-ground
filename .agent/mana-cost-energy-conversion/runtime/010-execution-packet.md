# Task Execution Packet

## Task
010-skilldriver-spend-gated-cast.md

## Goal
Wire SkillDriver root casts to external requests with caster/entity/mana/token and refund a rejected slot cooldown.

## Files Allowed To Modify
- SkillDriver/translator, player/mob roots, target rejection callback, and direct cast tests.

## Behavior To Preserve
- Ready casts reset cooldown optimistically; accepted casts spawn through the same simulation update; zero-cost uses the same uniform gate.

## Behavior To Change
- Root casts carry compiled ManaCost and proxy caster; rejection restores matching slot readiness.

## Dependencies Confirmed
- External gate accepts/rejects root requests and bridge calls ICombatTarget.ReceiveSpawnRejected.

## Validation Required
- Static call-path check, focused cast test where possible, and diff check.

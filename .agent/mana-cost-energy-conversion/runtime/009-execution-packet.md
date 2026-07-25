# Task Execution Packet

## Task
009-ecs-resource-spend-pipeline.md

## Goal
Insert a serial external root-spawn intake that spends caster Mana, emits existing internal spawn events on success, and returns rejections through presentation.

## Files Allowed To Modify
- Combat root, spawn event contracts/systems, presentation bridge, target interface, and focused tests.

## Files Allowed To Create
- External request/rejection contracts, gate system, and rejection bridge/lane if separate.

## Behavior To Preserve
- Existing internal interval, impact, and child paths append internal spawn events directly and never spend.

## Behavior To Change
- Managed root submission becomes a gated external request with caster, mana cost, and cast token.

## Dependencies Confirmed
- `Mana` is present on all target proxies and ECS owns its Current.

## Hard Boundaries
- Gate is serial, ordered before expansion, and uses existing scope buffers and singleton producer-handle discipline.

## Validation Required
- Focused gate tests, static internal-path review, and diff check.

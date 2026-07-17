# Task Execution Packet

## Task
006-validation-and-docs.md

## Goal
Polish validation checks and rewrite the VFX system doc to match ECS-owned data shapes.

## Files Allowed To Modify
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`
- `Docs/reference/simulation/vfx-system.md`
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Relevant Global Context
- Shape contracts live in `VfxDataShapeTable`.
- Registration returns `0` on null asset or validation failure.
- Runtime has one root, one dispatcher, one producer handle, per-shape queues and bucketing.

## Acceptance Criteria
- Mismatched graph shape fails registration with descriptive error and id `0`.
- Doc has no stale single-payload or per-item-dequeue language.

## Validation Required
- Search docs for stale names.
- Run available compile validation or document external blockage.

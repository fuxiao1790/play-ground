# Task Execution Packet

## Task
001-data-shape-core.md

## Goal
Introduce the ECS-owned VFX data-shape vocabulary, encoded VFX id helpers, timing data, and per-shape singleton containers. Do not wire producers/consumers yet beyond renaming the current request type.

## Files Allowed To Modify
- `Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- Existing files that reference `AoeVfxSpawnRequest`, for rename-only compile fixes.
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- `Assets/Scripts/System/Vfx/AoeVfxEcsComponents.cs`
- VFX emit sites that currently mention `AoeVfxSpawnRequest`.

## Behavior To Preserve
- `AoeVfxIds` remains five `int` fields.
- Existing Basic queue path remains functionally equivalent until later tasks update bucketing/dispatch.
- Single `ProducerHandle` remains on the singleton.

## Behavior To Change
- Rename request struct to `VfxSpawnRequest`.
- Add `TimedVfxSpawnRequest`.
- Add `VfxTimingData`.
- Add Basic and Timed singleton queues/scratch containers.
- Add `VfxDataShapeTable` and encoded id helpers.

## Relevant Global Context
- `0` is no-VFX.
- Shape is encoded in VFX id high bits, local index in low bits.
- `VfxDataShapeTable` is the sole buffer contract declaration.
- Persistent native containers are allocated in `OnCreate` and disposed in `OnDestroy`.

## Dependencies Confirmed
- None. This is the first task.

## Step-By-Step Instructions
- Create `VfxDataShapes.cs` with shape enum, request structs, buffer descriptors, property-name constants, and id encode/decode helpers.
- Update `AoeVfxEcsComponents.cs` to keep `AoeVfxIds` and add `VfxTimingData`.
- Rename current singleton queue/scratch fields to Basic names and add Timed queue/scratch fields.
- Rename `AoeVfxSpawnRequest` references to `VfxSpawnRequest`.

## Acceptance Criteria
- Compiles.
- `VfxDataShapeTable` is the only declaration of each shape's buffer set.
- Encoded ids round-trip for Basic and Timed and remain positive/nonzero for local indexes >= 1.
- Per-shape containers allocate/dispose symmetrically.

## Validation Required
- Run compile/build after task.
- Search for stale `AoeVfxSpawnRequest`.

## Hard Boundaries
- Do not update root registration/validation/dispatch for shapes in this task.
- Do not update authoring shape selectors in this task.
- Do not change architecture or combine later tasks.

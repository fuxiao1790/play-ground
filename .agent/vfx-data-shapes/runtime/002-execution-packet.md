# Task Execution Packet

## Task
002-root-dispatcher-shapes.md

## Goal
Make registration, validation, resources, and dispatch shape-aware. One root, one dispatcher, one encoded id space partitioned by shape.

## Files Allowed To Modify
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Vfx/VfxDataShapes.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`

## Behavior To Preserve
- `Register(null, ...)` returns `0`.
- Same asset returns the first registered id.
- Root remains sole resource/id owner.
- Basic graph dispatch uploads positions, area sizes, spawn count, and `OnSpawn`.

## Behavior To Change
- `Register` accepts `VfxDataShape`.
- Ids are encoded with shape/local index.
- Resources live in per-shape owner lists.
- Validation uses `VfxDataShapeTable` and rejects missing, wrong-typed, and unexpected extra `GraphicsBuffer` properties.
- Timed dispatch uploads duration and tick interval buffers too.

## Relevant Global Context
- Shape buffer contracts are declared once in `VfxDataShapeTable`.
- `0` remains no-VFX.
- `DecodeLocalIndex(id) - 1` indexes into the owning shape list.
- GPU buffers grow in lockstep and release symmetrically.

## Dependencies Confirmed
- `VfxDataShapeTable` and encoded id helpers exist in `VfxDataShapes.cs`.
- `CombatAoeVfxDispatchSingleton` has Basic and Timed containers.

## Step-By-Step Instructions
- Add `Shape`, duration buffer, and tick interval buffer to `AoeVfxTypeResources`.
- Allocate/grow/release exactly the buffers required by the resource shape.
- Change validation to `ValidateGraphContract(asset, shape, out reason)`.
- Change `CombatVfxRoot` to keep per-shape owner lists and encoded ids.
- Add `RegisteredCountFor(shape)`, `DrainAndDispatchBasic`, and `DrainAndDispatchTimed`.

## Acceptance Criteria
- Compiles.
- Basic graphs own two buffers; Timed graphs own four.
- Registration fails on contract mismatch and returns `0`.
- Returned ids decode to requested shape/local index.
- Upload core is shared.

## Validation Required
- Run available compile validation or document external blockage.
- Search old `Register(... requireAreaSizeContract` callers for later task awareness.

## Hard Boundaries
- Do not update emit sites to shape-aware timed queues in this task.
- Do not update authoring shape selectors in this task.
- Do not remove pulse path.

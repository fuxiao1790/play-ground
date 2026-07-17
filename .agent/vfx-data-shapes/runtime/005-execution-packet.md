# Task Execution Packet

## Task
005-authoring-per-graph-shape.md

## Goal
Add per-slot VFX shape authoring and thread it through definitions, runtime compilation, and registration.

## Files Allowed To Modify
- `Assets/Scripts/Skills/Validator/BasicAoePrefab.cs`
- `Assets/Scripts/Skills/Validator/LingeringAoePrefab.cs`
- `Assets/Scripts/Skills/SkillDefinition.cs`
- `Assets/Scripts/Skills/SkillSetCompiler.cs`
- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Behavior To Preserve
- Existing prefabs default every slot to `Basic`.
- Pulse slot and pulse system remain.
- `AoeVfxIds` remains five ints.

## Behavior To Change
- Each VFX slot has an authored `VfxDataShape`.
- Compiled runtime AOEs carry five shape properties.
- Registration passes each effect's authored shape.

## Relevant Global Context
- Shape is per graph/slot and encoded into the returned id by `CombatVfxRoot.Register`.
- Timed emits carry `VfxTimingData` values populated by task 004.

## Dependencies Confirmed
- `CombatVfxRoot.Register(asset, shape)` exists.
- Runtime VFX emits decode shape from id.

## Step-By-Step Instructions
- Add serialized shape fields/defaults/getters to AOE prefabs.
- Add abstract shape getters to `AoeDefinitionBase`.
- Carry shapes through `RuntimeAoeDefinition` and `SkillSetCompiler`.
- Use runtime shape properties in `RegisterAoeVfx`.

## Acceptance Criteria
- Existing content defaults to Basic.
- Timed shape selection registers a Timed graph and makes emitted id route to Timed queue.
- Pulse slot stays.

## Validation Required
- Search registration for hard-coded Basic fallback.
- Run available compile validation or document external blockage.

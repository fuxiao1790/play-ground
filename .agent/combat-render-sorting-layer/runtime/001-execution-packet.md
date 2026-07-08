# Task Execution Packet

## Task
001-add-sorting-layers.md

## Goal
Add `CombatVfx` and `CombatSprites` Sorting Layers below `Default`.

## Files Allowed To Modify
- `ProjectSettings/TagManager.asset`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- Project prefabs and scenes with `m_SortingLayerID`.

## Behavior To Preserve
- Player and mob renderers remain on `Default`.

## Behavior To Change
- Sorting layer order becomes `CombatVfx`, `CombatSprites`, `Default`.

## Relevant Global Context
- Sorting Layers are the single tier mechanism.
- Default remains above the two new layers.

## Dependencies Confirmed
- None.

## Step-By-Step Instructions
- Check existing Sorting Layer usage.
- Add two layers before Default.

## Acceptance Criteria
- `m_SortingLayers` lists `CombatVfx`, `CombatSprites`, `Default`.
- Existing renderer assets do not use non-Default Sorting Layers.

## Validation Required
- Search project for `m_SortingLayerID`.

## Hard Boundaries
- Do not change GameObject layers.
- Do not move player or mob renderers off Default.

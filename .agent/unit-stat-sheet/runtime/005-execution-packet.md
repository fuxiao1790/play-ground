# Task Execution Packet

## Task
005-author-assets-and-wiring.md

## Goal
Create `UnitStatSheet` assets in the Unity editor and assign them on player/mob prefabs.

## Files Allowed To Modify
- Unity editor-authored assets and prefabs only.

## Files Allowed To Create
- `Assets/ScriptableObjects/Units/`
- `PlayerStatSheet.asset`
- `SlimeStatSheet.asset`
- `SkeletonStatSheet.asset`
- `BatStatSheet.asset`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Docs/reference/game-logic/mobs.md`
- Player prefab
- Mob prefabs

## Behavior To Preserve
- In-scene values should match previous serialized root values before tuning.
- Player uses the same `PlayerStatSheet` on `PlayerRoot` and `SkillDriver`.

## Behavior To Change
- Authored player/mob prefabs gain non-null `statSheet` references.

## Relevant Global Context
- `.asset` files must be created in the Unity editor, not hand-written.
- Code tasks 001-004 introduced the serialized fields that need editor wiring.

## Dependencies Confirmed
- Tasks 001-004 are implemented by source.
- `UnitStatSheet` type exists.
- Player and mob root/driver serialized fields exist.
- `Assets/ScriptableObjects/Units/` does not exist yet.
- No existing `UnitStatSheet` assets or `statSheet` prefab references were found under `Assets/ScriptableObjects`, `Assets/Prefabs`, or `Assets/Scenes`.

## Step-By-Step Instructions
- In Unity editor, create `Assets/ScriptableObjects/Units/`.
- Create the four stat sheet assets through `Create > PlayGround > Units > Unit Stat Sheet`.
- Set values from task 005 and `Docs/reference/game-logic/mobs.md`.
- Assign player sheet to `PlayerRoot.statSheet` and `SkillDriver.statSheet`.
- Assign archetype sheets to mob prefabs.

## Acceptance Criteria
- Every authored player/mob prefab has a non-null `statSheet`.
- In-scene behavior matches previous serialized values before tuning.

## Validation Required
- Unity editor compile.
- PlayMode checks listed in task 005.

## Hard Boundaries
- Do not hand-write Unity `.asset` files.
- Do not hand-edit prefab YAML to fake editor wiring.
- Stop if Unity editor authoring is unavailable.

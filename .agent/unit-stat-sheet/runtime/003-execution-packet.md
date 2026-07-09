# Task Execution Packet

## Task
003-playerroot-consumes-sheet.md

## Goal
Make `PlayerRoot` read vitals and movement from `UnitStatSheet` instead of duplicated root fields.

## Files Allowed To Modify
- `Assets/Scripts/Player/PlayerRoot.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Player/PlayerMovement.cs`
- `Assets/Scripts/Player/PlayerHealth.cs`

## Behavior To Preserve
- Current health remains mirrored from ECS through `ReceiveCombatTick`.
- Dash, stop threshold, acceleration/friction, hurt flash, and target radius stay on `PlayerRoot`.
- Target proxy lifecycle stays unchanged.

## Behavior To Change
- Player max health and move speed come from `statSheet`.
- Missing player stat sheet fails fast in `Awake`.

## Relevant Global Context
- GameObject root still owns max health and seeds ECS through `CombatMaxHealth`.
- Sheet owns authored max health and move speed.

## Dependencies Confirmed
- Task 001 complete: `UnitStatSheet` exists and exposes `MaxHealth` and `MoveSpeed`.

## Step-By-Step Instructions
- Add `using PlayGround.Common.Stats;`.
- Add `[SerializeField] private UnitStatSheet statSheet;`.
- Remove serialized `moveSpeed` and `maxHealth`.
- Validate `statSheet != null` in `Awake`.
- Change `CombatMaxHealth` to `statSheet.MaxHealth`.
- Construct `PlayerMovement` with `statSheet.MoveSpeed`.
- Construct `PlayerHealth` with `statSheet.MaxHealth`.

## Acceptance Criteria
- Compiles.
- Player target max health comes from `statSheet.MaxHealth`.
- Player movement speed comes from `statSheet.MoveSpeed`.
- No remaining references to removed player `maxHealth`/`moveSpeed` fields.

## Validation Required
- Search `PlayerRoot.cs` for removed field names/usages.
- Compile/build if available.

## Hard Boundaries
- Do not modify files outside the allowed list except compile fixes directly caused by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.

# Task Execution Packet

## Task
004-mobroot-consumes-sheet.md

## Goal
Make `MobRoot` read vitals and movement from `UnitStatSheet`, including runtime clones for code-built mobs.

## Files Allowed To Modify
- `Assets/Scripts/Mob/MobRoot.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Common/Stats/UnitStatSheet.cs`
- `Assets/Scripts/Spawn/MobSpawnerRoot.cs`
- Mob PlayMode tests that call `ConfigureAuthoring`.

## Behavior To Preserve
- Max-health seeding into ECS and current-health mirror path stay unchanged.
- Target radius and wander tuning remain on `MobRoot`.
- Code-built mobs still work through `ConfigureAuthoring`.

## Behavior To Change
- Mob max health and speed come from `statSheet`.
- `ConfigureAuthoring` creates a runtime `UnitStatSheet` clone and sets vitals.
- Missing authored mob sheet fails fast in `Awake`.

## Relevant Global Context
- Authored sheet assets are never mutated at runtime.
- Runtime clone mutation is allowed for code-created mobs.
- Sheet owns authored max health and move speed.

## Dependencies Confirmed
- Task 001 complete: `UnitStatSheet` exists and exposes `MaxHealth`, `MoveSpeed`, and internal `SetRuntimeValues`.
- `MobSpawnerRoot.CreateRuntimeMobPrefab()` calls `ConfigureAuthoring(35f, 2.5f, 0.5f)`.

## Step-By-Step Instructions
- Add `using PlayGround.Common.Stats;`.
- Add `[SerializeField] private UnitStatSheet statSheet;`.
- Remove serialized `speed` and `maxHealth`.
- Redirect `Speed`, `MaxHealth`, and `CombatMaxHealth`.
- In `Awake`, validate sheet before reading it and seed `CurrentHealth` from `statSheet.MaxHealth`.
- In `ConfigureAuthoring`, create `ScriptableObject.CreateInstance<UnitStatSheet>()`, call `SetRuntimeValues`, and keep `targetRadius = radius`.
- Change internal speed usage to read `Speed`.

## Acceptance Criteria
- Compiles.
- Authored mobs use sheet max health and move speed.
- Code-built mobs/tests still work through runtime clone path.
- No remaining references to removed mob `maxHealth`/`speed` fields.

## Validation Required
- Search `MobRoot.cs` for removed field names/usages.
- Compile/build if available.

## Hard Boundaries
- Do not modify files outside the allowed list except compile fixes directly caused by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.

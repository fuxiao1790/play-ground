# Task Execution Packet

## Task

001-sever-sim-to-gamelogic-edges.md

## Goal

Remove the dead simulation-to-game-logic references from `CombatRoot` while all code remains in `PlayGround.Runtime`.

## Files Allowed To Modify

- `Assets/Scripts/System/Core/CombatRoot.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/System/Core/CombatRoot.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`

## Behavior To Preserve

- Live AOE registration through `CombatRoot.RegisterType(AoeTypeDefinition)`.
- All ECS runtime behavior.

## Behavior To Change

- Remove unused `using PlayGround.Skills;`.
- Remove the dead `configTypeIds` field and `RegisterConfig(AoeConfig)` method.

## Relevant Global Context

- Simulation must not depend on game logic. No new abstraction, data path, or runtime behavior is allowed.
- `AoeConfig` remains in its current location until task 002.

## Dependencies Confirmed

- `rg` found no caller of `RegisterConfig` or `configTypeIds` outside their declarations.
- `SkillDriver` uses the live `RegisterType(AoeTypeDefinition)` path.

## Step-By-Step Instructions

1. Remove the unused Skills import.
2. Remove `configTypeIds`.
3. Remove `RegisterConfig(AoeConfig)`.
4. Do not modify other code.

## Acceptance Criteria

- `CombatRoot.cs` does not name a `PlayGround.Skills` type or `AoeConfig`.
- `rg -n "RegisterConfig|configTypeIds" Assets Packages` returns no result.

## Validation Required

- Run the acceptance search.
- Run a relevant Unity compile check if available; otherwise state why it could not be run.

## Hard Boundaries

- Do not modify files outside the allowed list.
- Do not change architecture, introduce abstractions, or combine later tasks.
- Stop on architectural ambiguity.

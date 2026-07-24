# Implementation Context

## Architectural Decisions
- Create a strict package chain: UI -> Game Logic -> ECS Simulation.
- `AoeConfig` is game-logic ScriptableObject authoring; move it from sim without changing its GUID.
- Remove dead `CombatRoot.RegisterConfig` and `configTypeIds`; live registration uses `RegisterType(AoeTypeDefinition)`.
- Task 008 is implemented: Debugging pulls `CombatStatsSingleton` directly from the ECS world. Simulation no longer references Debugging.
- Task 009 follows the core package split as the exempt Debugging leaf.

## Global Invariants
- Preserve runtime behavior and existing Unity asset references.
- A layer may depend only downward; simulation may not reference game logic or UI.
- Jobs and systems use unmanaged ECS/native data only; no live authoring reads in simulation.

## Ownership Boundaries
- Simulation owns ECS runtime plus managed objects that exist only to serve ECS.
- Game logic owns skills, authoring ScriptableObjects, player-facing gameplay, stats, and status authoring.
- Shared sim primitives are `DamageSnapshot`, `GameplayTags`, and `GameplayLayers`.

## Data Flow
- Game logic translates authored AOE data into `AoeTypeDefinition` and calls `CombatRoot.RegisterType`.
- `AoeConfig` may depend on sim AOE contracts; sim must not depend on it.

## Lifecycle / Allocation Rules
- No runtime data shape or lifecycle change in tasks 001-003.

## ECS / Job / Threading Constraints
- Do not modify systems, jobs, native containers, or ECS data shapes in tasks 001-003.

## Determinism Requirements
- Preserve existing registration behavior; remove only dead registration code.

## Producer / Consumer Separation
- Keep authored AOE configuration separate from ECS spawn and apply contracts.

## Reused Mechanisms
- `CombatRoot.RegisterType(AoeTypeDefinition)` is the existing live registration API.
- Unity script GUIDs remain stable when `.cs` and `.cs.meta` move together.

## Introduced Mechanisms
- None for tasks 001-003; task 003 only records the later package ownership split.

## Validation Requirements
- Search for removed code and illegal upward imports after each relevant task.
- Unity must recompile without errors before declaring compile validation complete.
- Confirm moved `AoeConfig` assets retain their script GUID and inspector fields.
- Confirm no simulation file names `PerformanceText` or `CombatStatsBinding` before creating `PlayGround.Sim`.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Core/CombatRoot.cs`
- `Assets/Scripts/System/Aoes/AoeConfig.cs`
- `Assets/Scripts/Common/StatusEffects/StackingTriggerDef.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Common/DamageSnapshot.cs`
- `Assets/Scripts/Common/GameplayTags.cs`
- `Assets/Scripts/Common/GameplayLayers.cs`

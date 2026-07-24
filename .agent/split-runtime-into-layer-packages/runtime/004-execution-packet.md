# Task Execution Packet

## Task

004-create-sim-package.md

## Goal

Create `com.playground.sim` / `PlayGround.Sim`, then move the entire simulation tree and three shared primitives into it without altering runtime code.

## Files Allowed To Modify

- `Packages/manifest.json`
- `Packages/com.playground.sim/package.json`
- `Packages/com.playground.sim/Runtime/PlayGround.Sim.asmdef` and `.meta`

## Files Allowed To Move

- `Assets/Scripts/System/**` plus every paired `.meta`, except the already relocated `AoeConfig` (not present there).
- `Assets/Scripts/Common/DamageSnapshot.cs` plus `.meta`.
- `Assets/Scripts/Common/GameplayTags.cs` plus `.meta`.
- `Assets/Scripts/Common/GameplayLayers.cs` plus `.meta`.

## Files Allowed To Delete

- `Assets/Scripts/System/Application/CombatApplyFinalizeSystem.cs.delete`.

## Behavior To Preserve

- Every moved asset and script GUID.
- All namespaces, code, data shapes, and runtime behavior.

## Relevant Global Context

- Task 008 is validated: simulation no longer references Debugging.
- `PlayGround.Sim` has no `PlayGround.*` assembly references.
- Use Unity package dependencies needed by the moved code: Burst, Collections, Entities, Mathematics, Profiling Core, Render Pipelines Core/Universal, and Visual Effect Graph Runtime where required; do not add GameLogic/UI references.

## Dependencies Confirmed

- Tasks 001-003 complete.
- `PerformanceText` and `CombatStatsBinding` searches under sim are empty.

## Step-By-Step Instructions

1. Create package manifest and runtime asmdef.
2. Add `com.playground.sim: file:com.playground.sim` to the root manifest.
3. Delete only the named stale `.cs.delete` file.
4. Move the System folder and its folder `.meta` under package Runtime, retaining all children and their `.meta` files.
5. Move the three Common primitives and their `.meta` files under package Runtime/Common.
6. Do not move any other Common file or edit source code.

## Acceptance Criteria

- `PlayGround.Sim.asmdef` has no `PlayGround.*` reference.
- All moved `.cs` retain their original `.meta` GUID.
- No System source remains under `Assets/Scripts/System`.

## Validation Required

- Compare pre/post `.meta` GUID lists for moved scripts.
- Search asmdef references for `PlayGround.`.
- Run `git diff --check`.
- Do not launch Unity while the interactive editor is open; report Unity import validation as user-pending.

## Hard Boundaries

- Do not alter any C# source content or namespace.
- Do not move Game Logic or Debugging files; those belong to later tasks.
- Do not start tasks 005 or later.

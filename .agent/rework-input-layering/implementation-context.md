# Implementation Context

## Architectural Decisions
- UI Toolkit owns the only mouse fire source: a full-screen `GameplayInputSurface` behind `#bar`.
- `PlayerRoot` reads `IGameplayInputSource.FireHeld`; it no longer reads the `Attack` action.
- `IGameplayInputSource` is Game Logic-owned; UI implements and registers it, preserving `SkillUi -> GameLogic -> Sim`.
- UI Toolkit picking determines click-through. Modal editing is the only remaining input gate.

## Global Invariants
- Mouse-only fire; leave the input-actions asset and non-mouse bindings untouched.
- Preserve aim from `UI/Point`; preserve movement and dash behavior outside modal suspension.
- No UI references from Game Logic and no duplicate firing path.

## Ownership Boundaries
- `PlayerRoot` owns its registered fire source and modal-suspend state.
- `GameplayInputSurface` owns pointer callbacks, pointer capture, and held state.
- `SkillLoadoutUi` controls picker-modal suspension only.

## Data Flow
- UI pointer events -> `GameplayInputSurface.FireHeld` -> `PlayerRoot.Update` -> `SkillDriver.Tick`.
- Picker open/close -> `PlayerRoot.SetGameplayInputSuspended`.

## Lifecycle / Allocation Rules
- Self references and validation in `Awake`; cross-component registration and UI root access in `OnEnable`.
- Read fire once per frame in `Update`.
- Create UI element/callbacks once; no per-frame allocations or searches.
- Missing fire source warns once in `Start` and produces no fire.

## ECS / Job / Threading Constraints
- This work remains managed UI/Game Logic; it must not affect ECS jobs, data ownership, or structural changes.

## Determinism Requirements
- None beyond existing frame-based input sampling.

## Producer / Consumer Separation
- UI supplies intent only; `PlayerRoot` remains the Game Logic consumer.

## Reused Mechanisms
- UI Toolkit z-order, `PickingMode`, and pointer capture.
- Existing UI-to-`PlayerRoot` push wiring.
- Existing `SkillDriver.Tick(bool, ...)` API.

## Introduced Mechanisms
- `IGameplayInputSource` in `PlayGround.Player`.
- `GameplayInputSurface` in the Skill UI assembly.

## Validation Requirements
- Compile relevant Game Logic and Skill UI code.
- Static checks for removed Attack/gate API and correct source registration.
- Task 005 documents editor wiring and manual playtest; do not perform editor-only changes in code.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Player/IGameplayInputSource.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/SkillUi/GameplayInputSurface.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`

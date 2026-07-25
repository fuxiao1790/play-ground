# Task Execution Packet

## Task
002-world-click-surface.md

## Goal
Add the bottom-layer UI Toolkit surface that tracks world-pointer holds and supplies `IGameplayInputSource` to `PlayerRoot`.

## Files Allowed To Modify
- `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uss`

## Files Allowed To Create
- `Assets/Scripts/SkillUi/GameplayInputSurface.cs`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/Player/IGameplayInputSource.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Behavior To Preserve
- Skill UI remains visually unchanged and normal top-layer controls retain their own clicks.
- Fire source is mouse pointer UI Toolkit events only; aim remains elsewhere.

## Behavior To Change
- A transparent full-screen sibling behind `#bar` becomes the sole UI-provided held-fire source.

## Relevant Global Context
- Self references in `Awake`; root access and `PlayerRoot` registration in `OnEnable`.
- Use one cached element and callbacks; no `Update`, closures, or repeated searches.
- UI Toolkit z-order and `PickingMode.Position` decide whether clicks reach the surface.
- `PlayerRoot` must remain UI-agnostic; UI pushes its input source into it.

## Dependencies Confirmed
- `Assets/Scripts/Player/IGameplayInputSource.cs` exists and contains `bool FireHeld { get; }`.
- `PlayerRoot` currently lacks `SetFireInput`/`ClearFireInput`; task 003 is expressly co-authored to add those methods. This task must call the specified APIs but compilation is validated only after task 003 completes.

## Step-By-Step Instructions
1. Create `GameplayInputSurface` in `PlayGround.Skills` with the specified attributes and interface.
2. Resolve and validate `UIDocument` and `PlayerRoot` in `Awake`, including the one-time `FindAnyObjectByType<PlayerRoot>()` fallback.
3. In `OnEnable`, create/cache and insert a full-screen transparent surface at root index 0, register callbacks once, then register it with `PlayerRoot`.
4. In `OnDisable`, clear only its registration, clear held state, and remove the surface.
5. Capture pointer on down; release and clear on up only when held; clear on capture out.
6. Add the required full-screen USS class.

## Acceptance Criteria
- UI-layer component exposes held fire through the Game Logic contract.
- Empty-world press can set held fire, while top-layer controls win pointer picking.
- No per-frame allocations.

## Validation Required
- Static source checks now; compile after task 003 adds the registration API.

## Hard Boundaries
- Do not edit `PlayerRoot` or `SkillLoadoutUi`; those belong to tasks 003 and 004.
- Do not add input-system reads or new fire sources.

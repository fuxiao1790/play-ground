# Task Execution Packet

## Task

003-input-block-reasons.md

## Goal

Make gameplay input blocks compose between pause and skill picker, and stop held-pointer click leakage.

## Files Allowed To Modify

- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Ui/Hud/SkillLoadout/SkillLoadoutUi.cs`
- `Assets/Scripts/Ui/Hud/GameplayInputSurface.cs`

## Files Allowed To Create

- `Assets/Scripts/Player/GameplayInputBlock.cs`

## Behavior To Preserve

- Picker-only blocking at normal time.
- `PlayerRoot.Update` still reaches `SkillDriver.Tick` for queued edit processing.
- Surface remains sole click-to-fire input source.

## Behavior To Change

- Pause is an independent input-block reason.
- Movement, dash, aiming, and firing all respect block mask.
- Pausing ignores world surface and clears/relinquishes held pointer capture.

## Relevant Global Context

- UI may depend on Game Logic; Game Logic must not depend on UI.
- `PauseController` exists and publishes `PausedChanged`.
- Actor roots must stay enabled.
- Subscribe/unsubscribe in `OnEnable`/`OnDisable`; validate serialized dependencies in `Awake`.

## Dependencies Confirmed

- `Assets/Scripts/Game/PauseController.cs` provides `IsPaused`, `PausedChanged`, and `SetPaused`.

## Step-By-Step Instructions

1. Add `[Flags] GameplayInputBlock` (`None`, `Paused`, `SkillPicker`) in `PlayGround.Player`.
2. Replace player boolean with mask and public `GameplayInputBlocked`; add idempotent per-reason setter.
3. Use property for move, dash, aim, and fire reads.
4. Add serialized/validated `PauseController`; wire `PausedChanged` in enable/disable to Paused reason.
5. Migrate skill picker calls to SkillPicker reason.
6. Add serialized/validated `PauseController` to world input surface; on pause set surface `PickingMode.Ignore`, clear held state/release capture; resume restores `PickingMode.Position`; wire lifecycle subscription.

## Acceptance Criteria

- Reasons compose without closing picker clearing pause block.
- Held pointer produces no fire on resume.
- Pause blocks player move/dash/aim/fire; picker-only behavior remains.

## Validation Required

- Static inspection only. Unity test execution deferred to user.

## Hard Boundaries

- Do not change `PauseController`, task-002 guards, scene/prefab/assets, or ECS.
- Do not add a second pause state or direct Game Logic -> UI reference.

# Task Execution Packet

## Task

002-disable-root-controls-during-cooldown.md

## Goal

Make the skill, support, and support-cap controls for a direct root visibly disabled while its cooldown is running. Leave trigger controls enabled.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `Docs/contracts/skill-loadout-editing.md`

## Behavior To Preserve

- UI is a read-only projection and queues commands only.
- Trigger buttons remain editable during a root cooldown.
- Existing cooldown label updates and support-cap constraints remain correct.

## Behavior To Change

- Expose `SkillDriver.IsRootOnCooldown(int)` by reusing the private helper from task 001.
- Cache per-node skill, cap, and support buttons while rebuilding the bar.
- Update their enabled state each frame from driver cooldown status and capability helpers.

## Relevant Global Context

Task 001 is complete and contains private `IsRootNodeOnCooldown`. No UI-side cooldown state or new command path is permitted. Empty-node control references must remain null.

## Dependencies Confirmed

- `SkillDriver.IsRootNodeOnCooldown(int)` exists at `Assets/Scripts/Skills/SkillDriver.cs:406`.

## Step-By-Step Instructions

1. Add public `IsRootOnCooldown` adjacent to cooldown progress lookup.
2. Add and initialize the specified per-node button arrays/lists.
3. Add `CanIncreaseSupportCap` and `CanDecreaseSupportCap`; use them for initial cap state and per-frame state.
4. Cache newly created controls in `AddNodeColumn`, including each support button.
5. Extend `Update()` to refresh labels plus all non-null cached controls from `IsRootOnCooldown`.

## Acceptance Criteria

- A cooldown disables root skill/support/cap controls, but not trigger controls.
- Controls re-enable when ready.
- Triggered-only and empty nodes never throw or become improperly locked.

## Validation Required

- Static source check of public accessor, cached controls, and enabled-state logic.
- Run the focused PlayMode check if the Unity project lock permits it.

## Hard Boundaries

- Do not edit USS/UXML or task 003 picker-pending behavior.
- Do not add UI-owned cooldown state.
- Do not modify files outside the allowed list.

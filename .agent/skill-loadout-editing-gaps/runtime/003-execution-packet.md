# Task Execution Packet

## Task

003-picker-waits-for-edit-resolved.md

## Goal

Keep a picker open and pending after a command is queued. Close it only through the existing successful `LoadoutChanged` refresh; on rejection, re-enable it and show the driver-provided reason.

## Files Allowed To Modify

- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/SkillUi/SkillLoadoutUi.cs`
- `Docs/contracts/skill-loadout-editing.md`

## Behavior To Preserve

- UI only submits commands; it never mutates a loadout locally.
- Successful `LoadoutChanged` causes `RefreshBar()` and `ClosePicker()` before successful `EditResolved`.
- Existing cancel behavior remains unchanged when no command is pending.

## Behavior To Change

- `AddChoice` routes to `SubmitChoice`.
- Synchronous queue rejection leaves modal interactive and displays its reason.
- Queued edits disable the full modal pending result.
- Async edit rejection re-enables the modal and displays `RejectionReason`.

## Relevant Global Context

Contract event ordering is fixed: success emits `LoadoutChanged` first, then `EditResolved`. Do not add a new validation/eligibility system or UXML/USS status element; reuse `#title`.

## Dependencies Confirmed

- None functionally. Task 004 must follow this task because both touch picker choice creation.

## Step-By-Step Instructions

1. Make `AddChoice` call a new `SubmitChoice` helper.
2. Queue success sets pending; queue failure calls status helper.
3. Add `SetPickerPending` and `ShowPickerStatus` exactly as task describes.
4. Replace `OnEditResolved`: accepted returns; rejection clears pending and shows reason.

## Acceptance Criteria

- Queued choice does not close immediately and cannot double-submit.
- Success still closes by the existing refresh path.
- Driver or synchronous rejection leaves interactive modal with an error title.

## Validation Required

- Static source/order check and `git diff --check`.
- Unity test only if project lock no longer exists.

## Hard Boundaries

- Do not modify UXML/USS, task 004 support filtering, or UI ownership boundaries.
- Modify only the allowed file.

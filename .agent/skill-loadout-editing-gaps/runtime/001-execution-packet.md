# Task Execution Packet

## Task

001-preserve-root-cooldown-on-unrelated-edits.md

## Goal

Preserve each unchanged root's `SkillSlotState` across an accepted edit, while resetting only the edited root for skill/support/cap edits.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDriver.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Tests/PlayMode/AoePlayModeTests.cs`
- `Docs/contracts/skill-loadout-editing.md`

## Behavior To Preserve

- `CompileAndRegister()` remains a genuine private zero-argument method.
- Restore and initial startup compile fresh cooldown state.
- Existing revision gating, validation, compilation, and event ordering stay unchanged.

## Behavior To Change

- On a successful edit, map old cooldown states by stable root node index.
- Reuse every unchanged mapped state. Reset only the edited node when the edit affects root cooldown.
- Trigger commands never request a cooldown reset.

## Relevant Global Context

`SkillDriver` is the sole cooldown authority. Node indices are stable. The preservation context must be one-shot private fields consumed and cleared by compilation, retaining `slotStates` as the single source of truth.

## Dependencies Confirmed

- None: task 001 has no prerequisites.

## Step-By-Step Instructions

1. Add `preserveCooldownState` and `cooldownResetNodeIndex` by the pending-edit fields.
2. Extract `AffectsRootCooldown` and add `IsRootNodeOnCooldown`; use them from `IsCooldownBlocked`.
3. At `CompileAndRegister()` entry, capture and clear the flags. Reuse old states by node index except the requested reset node.
4. Immediately before successful edit recompilation, set preservation flags according to the command kind.
5. Do not change startup or restore compilation calls.

## Acceptance Criteria

- Unrelated root cooldown stays unchanged; edited root resets.
- Trigger edits reset no root cooldown.
- Restore has fresh states.
- Reflection-based zero-argument compile test still passes.

## Validation Required

- Static source check for a single zero-argument `CompileAndRegister` method and the preservation flow.
- Run the relevant PlayMode test if available.

## Hard Boundaries

- Do not modify files outside the allowed list.
- Do not add a `CompileAndRegister` overload or parameters.
- Do not implement later UI/tag/doc tasks.

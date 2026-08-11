# Task Execution Packet

## Task

002-zero-dt-driver-guards.md

## Goal

Make non-dt-gated driver work inert for zero-delta frames without injecting pause state.

## Files Allowed To Modify

- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Spawn/ContinuousStreamBehaviour.cs`
- `Assets/Scripts/Player/PlayerVfxAura.cs`

## Files Allowed To Create

- `Assets/Tests/EditMode/ContinuousStreamBehaviourEditModeTests.cs`

## Behavior To Preserve

- `SkillDriver.ProcessPendingEdit()` resolves queued edits before firing/cooldown work.
- Positive-delta cooldown and spawn behavior remains unchanged.
- Actor roots remain enabled.

## Behavior To Change

- Skill cooldown/casts, continuous spawn draining, and aura emission do nothing for `dt <= 0`.

## Relevant Global Context

- Time scaling is the single clock; no pause dependencies or flags belong in drivers.
- No Unity tests may be run by agent; static validation only.

## Dependencies Confirmed

- None: task is independent.

## Step-By-Step Instructions

1. In `SkillDriver.Tick`, cache `Time.deltaTime` after `ProcessPendingEdit()` and null guard; return if non-positive; use cache in slot ticks.
2. In continuous stream runtime `Tick`, return on non-positive `dt` before accumulator/sink work.
3. In aura `Update`, return on non-positive `Time.deltaTime` before time/emit checks.
4. Add EditMode test showing saturated accumulator has no spawn at zero dt and positive-dt behavior remains correct.

## Acceptance Criteria

- Ready held slots do not spawn when time is frozen.
- Queued edits still resolve frozen.
- Positive-delta cooldown behavior unchanged.
- Saturated stream produces no zero-dt spawn, covered by EditMode test.

## Validation Required

- Static source inspection. User must execute Unity tests and provide XML result files.

## Hard Boundaries

- Do not change roots, ECS, or pause-controller code.
- Do not move `ProcessPendingEdit` below a zero-dt guard.

# Task Execution Packet

## Task
002-verify-overlap.md

## Goal
Verify with Unity Profiler that `CombatRenderPrepareSystem:RenderPrepareJob` overlaps presentation main-thread systems, or apply fallback ordering only if the profiler shows the bridge completes it early.

## Files Allowed To Modify
- `.agent/render-prepare-overlap/index.md`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs` only if fallback is proven necessary.

## Files Allowed To Create
- Optional profiler summary artifact if capture data is available.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs`

## Behavior To Preserve
- Keep `OrderFirst` ordering unless profiler evidence shows overlap collapsed at `CombatApplyBridge`.

## Behavior To Change
- If overlap collapses, replace `OrderFirst` with explicit ordering after `CombatApplyBridge` and before `CombatVfxDispatchSystem`.
- Record final ordering in `index.md`.

## Relevant Global Context
- The unverified invariant is whether anything between prepare and submit forces a global sync.
- Fallback keeps VFX-dispatch overlap but sacrifices bridge overlap.

## Dependencies Confirmed
- Requires task 001 completed and validated.

## Step-By-Step Instructions
- Capture Unity Profiler on a heavy scene.
- Locate `CombatRenderPrepareSystem:RenderPrepareJob (Burst)` on worker threads.
- Determine whether the job ends at batched render join or earlier at bridge.
- Apply fallback only on observed early bridge join.
- Record final ordering in `index.md`.

## Acceptance Criteria
- Profiler capture or summary shows overlap with at least `CombatVfxDispatchSystem`.
- Net main-thread frame time improves and no new early stall appears.
- Final ordering is recorded in `index.md`.

## Validation Required
- Unity Profiler capture. If unavailable in this environment, report exact blocker and do not invent evidence.

## Hard Boundaries
- Do not apply fallback without profiler evidence.
- Do not claim profiling passed without a capture.
- Stop if capture cannot be produced.

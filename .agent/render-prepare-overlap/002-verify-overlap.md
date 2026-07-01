# 002 — Verify overlap and choose final ordering

## Scope
Verification + a conditional one-line ordering tweak. Low complexity.

## Why
The whole benefit rests on one unverified invariant: nothing between the prepare
system and the render system triggers a global job sync that completes
`RenderPrepareJob` early. Analysis says `CombatApplyBridge`
(`EntityManager.GetComponentObject<TargetCompanion>`, no structural change) and
`CombatVfxDispatchSystem` (no ECS access) do not — but this must be seen in a
capture, not assumed.

## Steps
1. Capture the Unity Profiler on a representative heavy scene (many projectiles +
   AOEs, matching the original capture).
2. On a worker-thread track, locate `CombatRenderPrepareSystem:RenderPrepareJob
   (Burst)` and check where it *ends*:
   - **Overlap achieved:** the job spans the `CombatApplyBridge` and
     `CombatVfxDispatchSystem` main-thread regions and is joined at
     `CombatBatchedRenderSystem` (its `JobHandle.Complete` / `WaitForJobGroupID`).
     Frame time should drop by roughly the previously-idle bubble width
     (~1.0–1.2 ms).
   - **Overlap collapsed:** the job completes at `CombatApplyBridge` (early join).
     Then apply the fallback below.

## Fallback (only if overlap collapses at the bridge)
Change `CombatRenderPrepareSystem` ordering from `OrderFirst = true` to:
```csharp
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(CombatApplyBridge))]
[UpdateBefore(typeof(CombatVfxDispatchSystem))]
```
This forgoes the ~0.498 ms bridge bubble but still guarantees the ~0.683 ms
VFX-dispatch overlap (that system does no ECS access and no sync, so it cannot
complete the job early). Re-capture to confirm.

## Acceptance criteria
- A profiler capture is attached/summarized showing the prepare job running
  concurrently with at least `CombatVfxDispatchSystem`, ideally also
  `CombatApplyBridge`.
- Net main-thread frame time is measurably lower than the pre-change capture, with
  no new `WaitForJobGroupID` stall appearing earlier in the frame (which would
  indicate a system now blocking on the prepare job).
- Final ordering (`OrderFirst` vs. the fallback attributes) recorded in
  `index.md`.

## Dependencies
Requires task 001.

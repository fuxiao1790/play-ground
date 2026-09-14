# 001 — InternalsVisibleTo Bridge File

## Goal
Remove the compile-time ceiling on what `AgentVFX.Editor`/`AgentVFX.Commands`
can reference inside `Unity.VisualEffectGraph.Editor`, so later tasks can
call non-public VFX Graph members without a new bridge method per member.

## Dependencies
None. First task.

## Files to create
- `Assets/AgentVFX/InternalAccess/ExposeAgentVfxInternals.cs`
  ```csharp
  using System.Runtime.CompilerServices;

  [assembly: InternalsVisibleTo("AgentVFX.Editor")]
  ```
  This file lives under the `Unity.VisualEffectGraph.Editor.asmref`
  (same folder as `AgentVfxInternalBridge.cs`), so it compiles into
  `Unity.VisualEffectGraph.Editor` itself, per
  [AgentVfxInternalBridge.cs:9-11](../../Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs#L9-L11).
- Matching `.meta` file (create via normal Unity import — do not hand-author
  a GUID; if this is applied outside the Editor, let Unity generate the
  `.meta` on next asset refresh rather than copying another file's GUID).

## Files to check, not necessarily change
- `Assets/AgentVFX/Editor/AgentVFX.Editor.asmdef` — already references
  `Unity.VisualEffectGraph.Editor` (confirmed). No change expected.

## Acceptance Criteria
- New file compiles as part of `Unity.VisualEffectGraph.Editor`.
- Smoke test: temporarily add (per guide §2) a static method in
  `AgentVFX.Editor` that references an internal `UnityEditor.VFX` member
  (e.g. `VFXContext.inputFlowSlot`'s backing internal type, or any internal
  member on `VFXGraph`) and confirm it compiles. Remove the scratch method
  before finishing the task — it was only to prove the bridge works; task
  002+ exercises the real internal access.
- `unity command vfx_ping` still returns `UnityEditor.VFX.VFXGraph` (this
  task must not break existing behavior).

## Scope
Small. One new file, no logic.

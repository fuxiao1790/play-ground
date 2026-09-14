# Implementation Log

## Status
Complete and verified. All of tasks 001-007 compile clean against Unity
6000.4.8f1 / VFX Graph 17.4.0 / `com.unity.pipeline` 0.7.0-exp.1. `unity command
vfx_ping` works end to end (CLI -> Pipeline -> AgentVfxCommands ->
AgentVfxApi -> AgentVfxInternalBridge -> UnityEditor.VFX). Second
`AgentVfxCompatibilityTests` run (2026-09-14 18:06): 13/13 passing, 1 legitimate
`Assert.Ignore` skip (not a bug -- see test source).

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-internal-access-core.md | Done | asmref, bridge core, id map, path guard |
| 002-type-library-and-node-ops.md | Done | |
| 003-slot-and-blackboard-ops.md | Done | |
| 004-compile-and-errors.md | Done | |
| 005-editor-api-and-dtos.md | Done | |
| 006-cli-commands.md | Written, unverified | Blocked on user installing `com.unity.pipeline` |
| 007-preview-and-tests.md | Done | Preview API not source-verified (native module); tests written, not run |

## Files Changed

- `Assets/AgentVFX/InternalAccess/Unity.VisualEffectGraph.Editor.asmref`
- `Assets/AgentVFX/InternalAccess/AgentVfxPathGuard.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxSnapshots.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxNodeOps.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxSlotOps.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxBlackboardOps.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxCompileOps.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxJson.cs`
- `Assets/AgentVFX/Editor/AgentVFX.Editor.asmdef`
- `Assets/AgentVFX/Editor/AgentVfxDtos.cs`
- `Assets/AgentVFX/Editor/AgentVfxApi.cs`
- `Assets/AgentVFX/Editor/AgentVfxPreview.cs`
- `Assets/AgentVFX/Commands/AgentVFX.Commands.asmdef`
- `Assets/AgentVFX/Commands/AgentVfxCommands.cs`
- `Assets/Tests/EditMode/AgentVfxCompatibilityTests.cs` (new)
- `Assets/Tests/EditMode/PlayGround.Tests.EditMode.asmdef` (added `AgentVFX.Editor` reference)

## Validation

Not run. Per `Docs/project-overview.md`, agents never invoke the Unity test
runner or compiler. Every internal-access API call in
`Assets/AgentVFX/InternalAccess/` was checked line-by-line against the actually
installed package source
(`Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/`) during
this session -- signatures, field names (including exact case, e.g.
`m_ExposedName`/`m_Exposed`), and access modifiers were read, not assumed. The
one exception is `Assets/AgentVFX/Editor/AgentVfxPreview.cs`
(`UnityEngine.VFX.VisualEffect` is a native engine-module binding with no local
C# source to grep) and all of `Assets/AgentVFX/Commands/AgentVfxCommands.cs`
(`Unity.Pipeline.Commands` is not installed).

## User's Next Steps

1. Install Unity CLI + run `unity pipeline install` in this project (per your
   prior decision to run this yourself).
2. Open the Unity Editor once to let everything compile.
3. Fix whatever the compiler flags in `Assets/AgentVFX/Commands/AgentVfxCommands.cs`
   only -- it's isolated in its own assembly precisely so this doesn't touch
   anything else. `Assets/AgentVFX/Editor/AgentVfxPreview.cs` may also need a
   small signature fix (`VisualEffect.Simulate`/`Reinit`).
4. Run `unity command vfx_ping` -- should return `UnityEditor.VFX.VFXGraph`.
5. Run `AgentVfxCompatibilityTests` (EditMode) and export the XML result under
   `Logs/` per `Docs/testing.md`; report back so results can be reviewed.

## First Real Test Run (2026-09-14)

`AgentVfxCompatibilityTests`: 11 passed, 2 failed, 1 skipped (honest `Assert.Ignore`,
not a bug). Two real bugs found and fixed:

1. **`CanCreateBlock`** -- `VFXModel.AddChild` threw a raw `ArgumentException`
   ("Cannot attach FlipbookPlay to VFXBasicSpawner"): not every block type is
   valid inside every context type, and neither `vfx_type_describe` nor
   `vfx_node_create` knew about it. Fixed by reading `VFXBlock.compatibleContexts`
   (a `[Flags] VFXContextType`) against `VFXContext.contextType`:
   `AgentVfxNodeOps.CreateNode` now rejects an incompatible pairing with a clear
   `InvalidOperationException` instead of letting Unity's exception through, and
   `DescribeNodeType` now returns `validContexts` (guide §12's own example schema
   had this field all along -- it was dropped in the first pass). Test rewritten
   to probe real context/block pairs via the API instead of assuming the first of
   each is compatible.
2. **`CanSetValue`** -- `AgentVfxJson.ToJson` wrapped scalars
   (`{"value":3.5}`) while `FromJson` expected a bare literal (`"3.5"`) as input:
   an asymmetric wire contract. Fixed `ToJson` to emit bare JSON literals for
   bool/int/float/string/enum, matching what `FromJson` already accepts, so a
   `SetSlotValue` -> `ReadSlot` round trip is textually stable.

## Blockers

- ~~`com.unity.pipeline` / Unity CLI not installed on this machine as of writing~~
  Resolved: Unity CLI installed at `%LOCALAPPDATA%\Unity\bin\unity.exe` (needed a
  full VS Code restart for PATH to pick it up); `unity pipeline install` added
  `"com.unity.pipeline": "0.7.0-exp.1"` to `Packages/manifest.json`. `com.unity.pipeline`
  is confirmed to be a real, installable package -- the index.md open question
  about whether it exists is answered.
- Current blocker: Unity Editor has not been opened against this project since
  the package was added, so `unity status` reports no connected instance and
  nothing under `Assets/AgentVFX/` has compiled yet. Opening the Editor is the
  next step (guide §5, §10) -- resolves the package and triggers the first
  compile of tasks 001-007.

## Deviations From Plan

- Split the CLI command layer into its own `AgentVFX.Commands` assembly
  (index.md described one `AgentVFX.Editor` assembly). This was not in the
  original plan but is a direct consequence of task 006 being unverified: it
  keeps a guaranteed-to-fail-until-Pipeline-is-installed file from blocking
  compilation of the verified DTO/API/bridge layer, which is otherwise usable
  and testable today.

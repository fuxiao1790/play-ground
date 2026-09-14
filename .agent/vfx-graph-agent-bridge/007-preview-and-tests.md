---
name: 007-preview-and-tests
description: Screenshot/preview loop (public APIs only) + EditMode compatibility tests
---

# 007 — Preview & Compatibility Tests

## Goal
Guide Phase 6 (visual feedback) and §27 (compatibility tests), so the agent can
close the loop by seeing results and so future Unity upgrades have a fast
health check.

## Files
- `Assets/AgentVFX/Editor/AgentVfxPreview.cs` — attach a `VisualEffect` to a
  preview `GameObject`/scene, play, capture via standard `Camera`/`Texture2D`
  readback (public APIs only, no internal access needed).
- `Assets/Tests/EditMode/AgentVfxCompatibilityTests.cs` — the guide's §27 list:
  `CanAccessVfxInternals`, `CanOpenGraph`, `CanEnumerateGraph`,
  `CanListNodeTypes`, `CanCreateContext`, `CanCreateBlock`, `CanCreateOperator`,
  `CanConnectCompatibleSlots`, `RejectsInvalidConnection`, `CanSetValue`,
  `CanSave`, `CanReload`, `CanCompile`, `CanReadCompilerErrors`.

## Test policy
Per `Docs/project-overview.md`: written here, never run by the agent. Hand the
exact EditMode class name to the user to run and export as XML under `Logs/`
per `Docs/testing.md`.

## Acceptance Criteria
- Preview capture returns a `Texture2D`/PNG bytes for a compiled graph attached
  to a `VisualEffect`.
- Test file compiles and lists all 13 tests (execution/result is the user's
  step).

## Dependencies
001-005. Independent of 006 (does not need the CLI layer to be useful).

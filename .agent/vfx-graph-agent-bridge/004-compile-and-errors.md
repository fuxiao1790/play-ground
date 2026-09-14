---
name: 004-compile-and-errors
description: vfx_compile / vfx_errors structured diagnostics
---

# 004 — Compile & Errors

## Goal
Give the agent a real compile+diagnose loop instead of console scraping (guide
§19).

## File
- `Assets/AgentVFX/InternalAccess/AgentVfxCompileOps.cs`

## Real API this maps to (verified)
- `VFXGraph.SetExpressionValueDirty()` then `VFXGraph.CompileForImport()`
  ([VFXGraph.cs:1239](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Models/VFXGraph.cs#L1239),
  [:1588](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Models/VFXGraph.cs#L1588)).
- `VFXGraph.errorManager` (`VFXErrorManager`) →
  `RefreshCompilationReport()`/`GenerateErrors()`
  ([VFXGraph.cs:544](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Models/VFXGraph.cs#L544)).
  Exact error-enumeration surface on `VFXErrorManager` must be read directly
  (`Editor/Core/VFXErrorManager.cs` — not yet inspected; do this at
  implementation time, not guessed).

## Acceptance Criteria
- A deliberately invalid graph (e.g. an unlinked required slot) produces a
  structured error list with a node ID and message, not a bare bool.
- A valid graph compiles with an empty error list.

## Dependencies
001, 002, 003.

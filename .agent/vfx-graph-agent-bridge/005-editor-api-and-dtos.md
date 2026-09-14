---
name: 005-editor-api-and-dtos
description: AgentVFX.Editor assembly, DTOs, stable public API over the bridge
---

# 005 — Editor API & DTOs

## Goal
Give the rest of the project (and the future CLI command layer) a boundary that
carries zero `UnityEditor.VFX` types (guide §14).

## Files
- `Assets/AgentVFX/Editor/AgentVFX.Editor.asmdef`
- `Assets/AgentVFX/Editor/AgentVfxDtos.cs` — `AgentVfxNodeDto`,
  `AgentVfxSlotDto`, `AgentVfxConnectionDto`, `AgentVfxGraphDto`,
  `AgentVfxTypeDto`, `AgentVfxCompileResultDto`, `AgentVfxErrorDto`
- `Assets/AgentVFX/Editor/AgentVfxApi.cs` — static class translating bridge calls
  (001-004) into/out of the DTOs above; this is what task 006's `[CliCommand]`s
  call.

## Acceptance Criteria
- No file under `Assets/AgentVFX/Editor/` references `UnityEditor.VFX`.
- Every bridge operation from tasks 001-004 has a corresponding `AgentVfxApi`
  method returning/accepting only DTOs or primitives.

## Dependencies
001, 002, 003, 004.

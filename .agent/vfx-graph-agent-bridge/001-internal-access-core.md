---
name: 001-internal-access-core
description: asmref merge, bridge skeleton, stable ID map, Ping, graph open/read/save, path-scope guard
---

# 001 — Internal Access Core

## Goal
Stand up the `.asmref` merge into `Unity.VisualEffectGraph.Editor` and the
`AgentVfxInternalBridge` skeleton: graph open/read/save, the stable ID map, and
the `Assets/VFX/**` / `Assets/AgentGenerated/**` path guard every later mutation
call reuses.

## Files to create
- `Assets/AgentVFX/InternalAccess/Unity.VisualEffectGraph.Editor.asmref`
- `Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs` — partial class,
  `Ping()`, `OpenGraph(path)`, `ReadGraph(path)` (topology walk), `SaveGraph(path)`
- `Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs` — `ConditionalWeakTable`-backed
  stable ID assignment/lookup for `VFXModel` and `VFXSlot`
- `Assets/AgentVFX/InternalAccess/AgentVfxPathGuard.cs` — validates an asset path
  falls under `Assets/VFX/` or `Assets/AgentGenerated/`

## Real API this maps to (verified)
- `VisualEffectResource.GetResourceAtPath(path)` → `.GetOrCreateGraph()`
- `VFXModel.children` / `GetRecursiveChildren()` for topology walk
- `AssetDatabase.SaveAssets()` for save
- `typeof(VFXGraph).FullName` smoke test (guide §8)

## Acceptance Criteria
- `Ping()` returns `typeof(VFXGraph).FullName` once compiled into
  `Unity.VisualEffectGraph.Editor`.
- ID map returns the same string for the same `VFXModel`/`VFXSlot` instance across
  repeated calls within one Editor session.
- Path guard throws a clear error for any path outside the two allowed roots.

## Dependencies
None — first task.

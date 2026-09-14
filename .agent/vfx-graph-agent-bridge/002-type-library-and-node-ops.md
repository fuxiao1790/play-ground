---
name: 002-type-library-and-node-ops
description: vfx_types_list/describe, node create/delete/move/configure
---

# 002 — Type Library & Node Ops

## Goal
Expose Unity's own node catalogue and let the agent create/delete/move/configure
nodes without any hard-coded effect knowledge (guide §11-12, §16).

## File
- `Assets/AgentVFX/InternalAccess/AgentVfxNodeOps.cs` (partial
  `AgentVfxInternalBridge`)

## Real API this maps to (verified)
- `VFXLibrary.GetContexts()/GetBlocks()/GetOperators()` → `VFXModelDescriptor<T>`
  (`name`, `category`, `modelType`) for `ListNodeTypes`/`DescribeNodeType`.
- `VFXModelDescriptor<T>.CreateInstance()` → node creation
  ([VFXLibrary.cs:141](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Core/VFXLibrary.cs#L141)).
- `VFXModel.AddChild(model, index, notify)` → insert into graph/context.
- `VFXModel.position` (`Vector2`) → node move.
- `VFXModel.SetSettingValue`/`SetSettingValues` → node configure.
- Delete = `parent.RemoveChild(model)` then
  `UnityEngine.Object.DestroyImmediate(model, true)` — confirmed pattern at
  [VFXViewController.cs:732](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/GraphView/Views/Controller/VFXViewController.cs#L732).

## Acceptance Criteria
- A block/operator/context type discovered via `ListNodeTypes` can be created,
  moved, configured, and deleted through the bridge with no type-specific code.
- Deleted nodes are actually destroyed (not left as orphaned sub-assets).

## Dependencies
001 (ID map, path guard, graph open/read).

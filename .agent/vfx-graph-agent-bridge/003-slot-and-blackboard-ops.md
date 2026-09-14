---
name: 003-slot-and-blackboard-ops
description: vfx_slot_read/set/connect/disconnect, vfx_blackboard_read/add/remove
---

# 003 — Slot & Blackboard Ops

## Goal
Slot discovery/connection (guide §17) and blackboard exposed-property management
(guide §11 Phase 4), both generic — no per-effect knowledge.

## Files
- `Assets/AgentVFX/InternalAccess/AgentVfxSlotOps.cs`
- `Assets/AgentVFX/InternalAccess/AgentVfxBlackboardOps.cs`

## Real API this maps to (verified)
- `IVFXSlotContainer.inputSlots`/`outputSlots` (`ReadOnlyCollection<VFXSlot>`),
  `GetNbInputSlots()`, `GetInputSlot(i)`/`GetOutputSlot(i)`.
- `VFXSlot.value` (get/set), `GetExpression()`, `CanLink(other)`, `Link(other)`,
  `Unlink(other)`, `HasLink()`.
- Blackboard property = `VFXParameter`; created via
  `ScriptableObject.CreateInstance<VFXParameter>()` + `Init(type)` (see
  `VFXModelDescriptorParameters.ParameterVariant.CreateInstance`,
  [VFXLibrary.cs:165-171](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Core/VFXLibrary.cs#L165-L171)),
  then `graph.AddChild(parameter)`; remove is the same
  RemoveChild+DestroyImmediate pattern as task 002.

## Acceptance Criteria
- Two compatible slots on different nodes can be connected/disconnected through
  the bridge; an incompatible connection returns a structured failure instead of
  throwing.
- A blackboard property can be added, read back, and removed.

## Dependencies
001, 002 (node handles to attach slots/parameters to).

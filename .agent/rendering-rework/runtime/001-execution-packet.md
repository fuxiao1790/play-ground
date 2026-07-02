# Task Execution Packet

## Task
001-batchid-to-component.md

## Goal
Convert `CombatRenderBatchId` from shared component data to plain per-entity component data.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `.agent/rendering-rework/implementation-log.md`

## Files Allowed To Create
- `.agent/rendering-rework/runtime/001-execution-packet.md`

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/rendering-rework/implementation-context.md`
- `.agent/rendering-rework/001-batchid-to-component.md`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Behavior To Preserve
- `CombatRenderBatchId.Value` remains the render resource id copied from spawn commands.
- Registry remains source of GPU resources.

## Behavior To Change
- `CombatRenderBatchId` no longer partitions chunks as `ISharedComponentData`.
- Equality/hash boilerplate is removed.

## Relevant Global Context
- One source of truth for per-entity render id.
- Spawn and render tasks later adapt all shared-component API usage.

## Dependencies Confirmed
- No prior task dependency.
- `CombatRenderBatchId` currently exists in `CombatRenderComponents.cs`.

## Step-By-Step Instructions
- Replace `ISharedComponentData, IEquatable<CombatRenderBatchId>` with `IComponentData`.
- Remove `Equals` and `GetHashCode`.
- Update ECS lifecycle comment to describe a per-entity render/resource id used at submit.

## Acceptance Criteria
- Type compiles as `IComponentData`.
- Later tasks remove remaining shared assumptions.

## Validation Required
- Search local declaration for `ISharedComponentData`.

## Hard Boundaries
- Do not modify files outside the allowed list except directly required compile fixes.
- Do not change architecture.
- Do not introduce new abstractions.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.

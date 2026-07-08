# Task Execution Packet

## Task
002-baked-capacity-mesh.md

## Goal
Replace the unit quad with a capacity-baked non-shrinking mesh.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- Atlas registration and UV basis data.
- 32-byte render component shape.

## Behavior To Change
- Shared mesh contains `capacity` repeated quads with UV1 slot indices.

## Relevant Global Context
- Mesh grows by doubling and never shrinks.
- Bounds use `BoundsHalfExtent * 2`.

## Dependencies Confirmed
- Self-contained.

## Step-By-Step Instructions
- Add capacity tracking and `EnsureMeshCapacity`.
- Build vertices, UV0, UV1, and indices for each slot.
- Set explicit large bounds.

## Acceptance Criteria
- Vertex count equals `capacity * 4`.
- Index count equals `capacity * 6`.
- Capacity is monotonically non-decreasing.

## Validation Required
- C# compile.

## Hard Boundaries
- Do not change ECS data shapes.
- Do not change shader yet.

# Task Execution Packet

## Task
002-instance-and-args-buffers.md

## Goal
Add `CombatInstanceData` and render-system-owned persistent instance/args buffers.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `.agent/combat-render-indirect/index.md`
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs`

## Behavior To Preserve
- Registry singleton creation.
- Active projectile and AOE queries.
- Presentation ownership of render submit resources.

## Behavior To Change
- Add 80-byte instance struct.
- Add `NativeList<CombatInstanceData>`, structured instance buffer, indirect args buffer, monotonic growth, and disposal.

## Relevant Global Context
- Buffers belong to `CombatBatchedRenderSystem`; registry remains static mesh/material/atlas owner.

## Dependencies Confirmed
- Data contract exists in index.

## Step-By-Step Instructions
- Add `[StructLayout(LayoutKind.Sequential)] CombatInstanceData`.
- Add persistent list and `GraphicsBuffer` fields.
- Assert `UnsafeUtility.SizeOf<CombatInstanceData>() == 80`.
- Create args buffer in `OnCreate`; instance buffer lazily on first active frame.
- Dispose resources in `OnDestroy`.

## Acceptance Criteria
- Struct size and field order match shader.
- Buffers grow to active count and are disposed.
- Args buffer holds one `IndirectDrawIndexedArgs`.

## Validation Required
- Runtime project compile.

## Hard Boundaries
- Do not add ECS components or alter spawn/apply archetypes.

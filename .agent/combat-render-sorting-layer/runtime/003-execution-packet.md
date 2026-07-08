# Task Execution Packet

## Task
003-persistent-mesh-renderer.md

## Goal
Add registry-owned `MeshRenderer` and `CombatRoot` sorting configuration.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRoot.cs`

## Files Allowed To Create
- None.

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `ProjectSettings/TagManager.asset`

## Behavior To Preserve
- Registry cleanup owns all created render resources.

## Behavior To Change
- Registry creates a persistent renderer GameObject using `SharedMesh` and `SharedMaterial`.
- `CombatRoot` defaults combat sprites to `CombatSprites`.

## Relevant Global Context
- The renderer is one atomic batch sorted by Sorting Layer.

## Dependencies Confirmed
- 001 adds `CombatSprites`.
- 002 adds `SharedMesh` capacity mesh.

## Step-By-Step Instructions
- Create `GameObject`, `MeshFilter`, and `MeshRenderer` in `EnsureSharedResources`.
- Add `ConfigureSorting`.
- Destroy renderer GameObject in `Unregister`.
- Add `CombatRoot` sorting fields and configuration call.

## Acceptance Criteria
- Runtime renderer uses `CombatSprites` layer and configured order.
- No orphan GameObject after unregister.

## Validation Required
- C# compile.

## Hard Boundaries
- Do not change spawn registration behavior.

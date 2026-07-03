# Task Execution Packet

## Task
004-registry-material.md

## Goal
Point the registry shared material at `Combat/AtlasIndirectSprite` and remove instancing flag.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Shaders/CombatAtlasIndirectSprite.shader`

## Behavior To Preserve
- Registry owns shared mesh/material.
- Registry sets `SharedMaterial.mainTexture` to packed atlas texture during registration.
- `Unregister()` destroys only registry-created resources.

## Behavior To Change
- `Shader.Find("Combat/AtlasIndirectSprite")`.
- Do not enable material instancing.

## Relevant Global Context
- Instance buffer is bound by render system through `RenderParams.matProps`, not by registry.

## Dependencies Confirmed
- New shader exists and is named `Combat/AtlasIndirectSprite`.

## Step-By-Step Instructions
- Replace shader lookup and error message.
- Remove `enableInstancing = true`.

## Acceptance Criteria
- Shared material uses indirect shader.
- No `enableInstancing` on registry material.

## Validation Required
- Runtime project compile.

## Hard Boundaries
- Do not alter atlas registration or UV math.

# Task Execution Packet

## Task
002-shrink-render-component.md

## Goal
Remove static UV payload from `CombatRenderComponent` and replace float-punned metadata with packed `RenderMeta`, reducing stride to 68.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`
- `.agent/combat-render-uv-lookup/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatRenderComponents.cs`
- `Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs`

## Behavior To Preserve
- `IsRenderable`, `RenderTypeId`, and `AlignToVelocity` public behavior.
- Projectile alignment to velocity and AOE non-alignment.
- Visual transform matrix fields used by render prep.

## Behavior To Change
- Component no longer carries `uvOriginU`/`uvV` or `UvOriginU`/`UvV` properties.
- Component carries `RenderMeta` after `objectToWorld`.
- Template builders stop copying UV basis to the component.
- Render prepare size assert becomes 68.

## Relevant Global Context
- Registry entries keep `UvOriginU`/`UvV`; only the ECS/GPU instance record loses them.
- `RenderMeta` high bit is align flag, low 31 bits are render id.
- Shader stride update happens in task 003.

## Dependencies Confirmed
- 001 complete: `CombatUvBasis` and `EnsureUvBasisBuffer()` added in registry.
- Code evidence: registry entries still retain `UvOriginU` and `UvV`.

## Step-By-Step Instructions
- Replace `uvOriginU`/`uvV` fields with `int RenderMeta`.
- Implement `RenderTypeId` and `AlignToVelocity` accessors using bit masks.
- Delete `UvOriginU`/`UvV` component properties.
- Remove UV initializers from `GetProjectileRenderComponent` and `GetAoeRenderComponent`.
- Update render prepare assert to 68.

## Acceptance Criteria
- `SizeOf<CombatRenderComponent>() == 68`.
- Metadata round-trips without cross-corruption.
- No remaining component UV field/property references.

## Validation Required
- Search for removed component UV members.
- Full compile after task 003/004.

## Hard Boundaries
- Do not alter registry UV entry storage.
- Do not modify shader/render system in this task except later direct lockstep task.
- Do not reopen architecture.

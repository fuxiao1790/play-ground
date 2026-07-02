# 006 — Remove the render element from AOE spawn

**File:** `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`

## Changes (verify line numbers with grep before editing)

- Remove `typeof(CombatRenderElement)` from all three archetypes:
  `lingeringArchetype` (~L56), `timedSpawnerLingeringArchetype` (~L72),
  `impactArchetype` (~L89).
- Remove the spawn-time seed in `RecordAoeReset`:
  `ecb.SetComponent(entity, CombatRenderMatrixUtility.ElementFor(kinematics, render));`
  (~L341). The first-frame matrix now comes from prepare (`OrderFirst`, same
  frame — see index risk note).
- In `AoeSpawnJob` remove:
  - `[NativeDisableContainerSafetyRestriction] ComponentTypeHandle<CombatRenderElement> RenderElementHandle;` (~L430)
  - `RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false)` (~L182)
  - `NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);` (~L454)
  - `renderElems[i] = CombatRenderMatrixUtility.ElementFor(kin, render);` (~L525)

Leave `CombatRenderComponent`, `CombatRenderActiveTag`, `CombatKinematicsComponent`,
and `CombatRenderBatchId` in place.

## Acceptance criteria

- AOE archetypes no longer include the render element; project compiles.
- AOEs render correctly on their first frame (validated by `AoePlayModeTests`).

## Dependencies

Depends on 001 (component + `ElementFor` removed).

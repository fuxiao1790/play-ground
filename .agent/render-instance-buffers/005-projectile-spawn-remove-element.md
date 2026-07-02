# 005 — Remove the render element from projectile spawn

**File:** `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`

## Changes (verify line numbers with grep before editing)

- Remove `typeof(CombatRenderElement)` from both archetypes:
  `CreateArchetype()` (~L408) and the child-spawner archetype (~L589).
- Remove the cold-create seed write `ecb.SetComponent(entity, new CombatRenderElement());`
  (~L264).
- In **both** reuse jobs remove the render-element plumbing:
  - `[NativeDisableContainerSafetyRestriction] ComponentTypeHandle<CombatRenderElement> RenderElementHandle;` fields (~L477, ~L700)
  - the `RenderElementHandle = GetComponentTypeHandle<CombatRenderElement>(false)` assignments (~L446, ~L630)
  - `NativeArray<CombatRenderElement> renderElems = chunk.GetNativeArray(ref RenderElementHandle);` (~L502, ~L727)
  - `renderElems[i] = new CombatRenderElement();` (~L542, ~L771)

Do **not** touch `ProjectileTag`, `CombatRenderComponent`, `CombatRenderActiveTag`,
`CombatRenderBatchId`, or the dead-slot reuse queries — pooling still keys on
those and is unaffected.

## Acceptance criteria

- Projectile archetypes no longer include the render element; project compiles.
- Spawn/reuse paths compile without the render-element handle; projectiles still
  spawn, move, and render (matrix now produced by prepare the same frame).

## Dependencies

Depends on 001 (component removed).

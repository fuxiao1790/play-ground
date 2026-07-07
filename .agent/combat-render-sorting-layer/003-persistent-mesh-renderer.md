# 003 - Persistent MeshRenderer + CombatRoot Sorting Fields

## Scope

`CombatRenderResourceRegistry.EnsureSharedResources()`
([CombatRenderComponents.cs:276-287](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L276-L287))
and `CombatRoot.cs`.

## Change

**Registry:**
- In `EnsureSharedResources()`, alongside building `SharedMesh`/`SharedMaterial`,
  create one persistent GameObject (e.g. `"Combat Sprite Renderer"`) owned by
  the registry, with a `MeshFilter` (`sharedMesh = SharedMesh`) and
  `MeshRenderer` (`sharedMaterial = SharedMaterial`).
- Add `ConfigureSorting(int sortingLayerID, int sortingOrder)`, mirroring
  `ConfigureAtlas`'s shape: sets `meshRenderer.sortingLayerID` /
  `meshRenderer.sortingOrder`.
- `Unregister()`
  ([CombatRenderComponents.cs:261-274](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L261-L274))
  must also destroy this GameObject (currently only destroys
  `SharedMaterial`/`SharedMesh`).

**CombatRoot:**
- Add two serialized fields next to `combatSpriteAtlas`
  ([CombatRoot.cs:37](../../Assets/Scripts/System/Common/CombatRoot.cs#L37)):
  `combatSpriteSortingLayerID` (use `[SerializeField] private SortingLayer` or
  store as `int` with a custom Inspector `SortingLayerField`-style drawer —
  match whatever convention the rest of the project uses for Sorting Layer
  fields; there is no existing precedent in this codebase, so a plain
  Inspector-assignable string/int identified by `SortingLayer.NameToID` is
  acceptable) and `combatSpriteSortingOrder` (int).
- Add `ConfigureRenderRegistrySorting()`, mirroring
  `ConfigureRenderRegistryAtlas()`
  ([CombatRoot.cs:670-673](../../Assets/Scripts/System/Common/CombatRoot.cs#L670-L673)),
  calling `_renderRegistry?.ConfigureSorting(...)`.
- Call it from the same place `ConfigureRenderRegistryAtlas()` is called
  ([CombatRoot.cs:95](../../Assets/Scripts/System/Common/CombatRoot.cs#L95)).
- Default the Inspector value to the `CombatSprites` Sorting Layer (added in
  001).

## Acceptance Criteria

- The combat sprite `MeshRenderer` reports `sortingLayerID` matching
  `CombatSprites` and the configured `sortingOrder` at runtime.
- `Unregister()` leaves no orphaned GameObject after `CombatRoot.OnDestroy()`.
- No visual regression yet — this only wires the renderer up; nothing writes
  per-frame instance data through it until 005 replaces the RendererFeature
  path.

## Dependencies

Depends on 001 (the `CombatSprites` Sorting Layer must exist to default to
it) and 002 (needs `SharedMesh` to exist to assign to the `MeshFilter`).

## Complexity

Small — mostly wiring, follows an existing pattern (`ConfigureAtlas`) in the
same file/class.

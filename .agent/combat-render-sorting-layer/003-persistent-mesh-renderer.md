# 003 - Persistent MeshRenderer + CombatRoot Wiring

## Status: revised after implementation (twice)

## Status update: SortingGroup proxy

As actually authored in the scene, sorting is not set directly on the
`combatSpriteRenderer`'s own `sortingLayerID`/`sortingOrder` fields. Instead a
`SortingGroup` component (`m_SortAtRoot: 1`) sits on the `MeshRenderer`'s
GameObject/parent, targeting the `CombatSprites` layer
(`m_SortingLayerID: -612406109`, the signed-int32 reading of that layer's
`uniqueID: 3682561187` in `TagManager.asset`), and the `MeshRenderer`'s
serialized `m_OwningSortingGroup` points at it
([BenchmarkLarge.unity:316-377](../../Assets/Scenes/BenchmarkLarge.unity#L316-L377)).
`Renderer.sortingLayerID`/`sortingOrder` are still overridden at runtime by
whichever `SortingGroup` owns a renderer — this is a supported, equivalent way
to assign a Sorting Layer to a `Renderer` without touching its own serialized
fields, and it's the pattern now used project-wide for every render tier (see
[[project_combat_render_sorting_group_proxy]]). No code changes; still
Inspector-only, per this doc's original decision.

## Status update: Material authoring

`CombatRenderResourceRegistry` used to build the shared `Material` itself
(`Shader.Find("Combat/AtlasIndirectSprite")` + `new Material(shader)` in
`EnsureSharedResources()`) and destroy it in `Unregister()` — both ran every
time render resources rebuilt (`BuildProjectileRenderResources`/
`TryBuildAoeRenderResource`, i.e. on every new skill-type registration), so a
new `Material` object was created and thrown away repeatedly at runtime for
no reason: the shader/material pairing is static, only `mainTexture`
(depends on the registered atlas) and the two `SetBuffer` calls (live
`GraphicsBuffer` handles) are genuinely procedural. Per user decision,
runtime injection is reserved for values that are actually procedural —
static config belongs in the Inspector.

Fixed: a Material asset using the `Combat/AtlasIndirectSprite` shader is
now assigned directly onto the persistent `combatSpriteRenderer`'s Material
slot in the Inspector (same object that already carries the `SortingGroup`).
`AttachRenderer` reads `meshRenderer.sharedMaterial` instead of constructing
one, throwing a clear `MissingReferenceException` if it's unassigned.
`Unregister()` no longer destroys or nulls it — only the code-built capacity
`Mesh` is destroyed there, since that's the only resource this registry
actually owns now
([CombatRenderComponents.cs:160-181,302-326](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L160-L181)).

The original version of this subtask had the registry instantiate the
`MeshFilter`/`MeshRenderer` GameObject at runtime (`new GameObject(...)` inside
`EnsureSharedResources()`) and push `sortingLayerID`/`sortingOrder` onto it from
a `string` field on `CombatRoot` resolved via `SortingLayer.NameToID` at
runtime. That shipped, but caused a real bug: a newly-added `[SerializeField]
string]` on an already-serialized `CombatRoot` instance got permanently
shadowed by a stale empty-string value baked into the scene before the code
default existed, `SortingLayer.NameToID("")` resolved to `0` (`Default`'s ID),
and the combat sprite batch silently rendered on `Default` instead of
`CombatSprites`. Revised design below removes the runtime string→ID resolution
entirely — the user decided Sorting Layer should be configured **only** via
the Inspector on a real, persistent Renderer, not centralized through
`CombatRoot`.

## Scope

`CombatRenderResourceRegistry`
([CombatRenderComponents.cs:122-330](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L122-L330))
and `CombatRoot.cs`.

## Change (as implemented)

**Registry:**
- No longer creates any GameObject. `EnsureSharedResources()` only builds
  `SharedMesh`/`SharedMaterial` and, if a renderer has already been attached,
  pushes them onto it.
- `AttachRenderer(MeshFilter meshFilter, MeshRenderer meshRenderer)` replaces
  the old `ConfigureSorting(int, int)` — it calls `EnsureSharedResources()`,
  stores the given (externally-owned) `MeshFilter`/`MeshRenderer`, and assigns
  `sharedMesh`/`sharedMaterial` onto them. It never touches
  `sortingLayerID`/`sortingOrder` — those are Inspector-only, forever.
- `Unregister()` no longer destroys a GameObject (doesn't own one); it clears
  `_meshFilter.sharedMesh`/`_meshRenderer.sharedMaterial` back to `null` so a
  destroyed procedural `SharedMesh` is never left dangling on a persistent
  scene object, then drops its own references so the next `AttachRenderer`
  call re-establishes them.

**CombatRoot:**
- Replaced `combatSpriteSortingLayerName`/`combatSpriteSortingOrder` with one
  serialized field: `combatSpriteRenderer` (`MeshRenderer`), pointing at a
  **persistent** GameObject (scene object or prefab instance) the project
  author places and configures directly — Sorting Layer, Order in Layer, and
  anything else Renderer-level is set in that GameObject's own Inspector.
- `ConfigureRenderRegistryRenderer()` (renamed from
  `ConfigureRenderRegistrySorting()`) fetches the `MeshFilter` off the same
  GameObject and calls `_renderRegistry?.AttachRenderer(meshFilter,
  combatSpriteRenderer)`. Logs a warning if `combatSpriteRenderer` is
  unassigned, an error if it has no `MeshFilter`.
- Called from every place the old sorting call was
  (`Awake`, `ConfigureAtlas`, `BuildProjectileRenderResources`,
  `TryBuildAoeRenderResource`).

## Authoring Requirement

A `MeshFilter` + `MeshRenderer` GameObject must exist in whatever scene/prefab
hosts `CombatRoot`, assigned to the `Combat Sprite Renderer` field, with its
Sorting Layer set to `CombatSprites` (added in 001) directly in the Inspector,
a `SortingGroup` targeting that layer (see the "SortingGroup proxy" update
above), and a Material using the `Combat/AtlasIndirectSprite` shader assigned
to its Material slot (see "Material authoring" above). This is now a one-time
authoring step per `CombatRoot` instance/prefab — there is no code-side
default to fall back on, and none is wanted (per user decision: flexibility
to configure sorting/material per-instance beats a centralized default).

## Acceptance Criteria

- No `SortingLayer.NameToID` call anywhere in this path — sorting is never
  touched by code.
- No `Shader.Find`/`new Material(...)` call anywhere in this path — the
  Material is never constructed by code.
- The assigned `MeshRenderer`'s Sorting Layer/Order and Material, as set in
  its own Inspector, are respected untouched at runtime except for
  `mainTexture` and the two `SetBuffer` calls, which are genuinely
  procedural.
- `Unregister()` leaves the persistent GameObject, its Sorting Layer, and its
  Material intact after `CombatRoot.OnDestroy()`; only the code-built
  `sharedMesh` is cleared/destroyed.
- If `combatSpriteRenderer` is unassigned, a clear warning fires and nothing
  throws (combat sprites simply don't draw, matching how a missing atlas is
  handled). If it's assigned but has no Material, `AttachRenderer` throws a
  clear `MissingReferenceException` (a missing Material is an authoring
  mistake on an object the project already committed to using, not a normal
  "feature not wired up yet" state).

## Dependencies

Depends on 001 (the `CombatSprites` Sorting Layer must exist so the authored
Renderer can be assigned to it) and 002 (needs `SharedMesh` to exist to assign
to the `MeshFilter`). No longer depends on any CombatRoot-owned sorting
fields.

## Complexity

Small — mostly wiring; slightly simpler than the original version since there
is no GameObject lifecycle or string/ID resolution to manage.

# Implementation Log

## Status
Complete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-add-sorting-layers.md | Complete | Added `CombatVfx`, `CombatSprites`, `Default` order in `TagManager.asset`; search found existing renderers using Default IDs only. |
| 002-baked-capacity-mesh.md | Complete | `CombatRenderResourceRegistry` now builds/grows a UV1 slot-index capacity mesh with large bounds. |
| 003-persistent-mesh-renderer.md | Complete (authoring revised) | Registry owns `Combat Sprite Renderer`; `CombatRoot` wires it up. Sorting is actually authored via a `SortingGroup` proxy on the renderer's GameObject, not the renderer's own fields. |
| 004-shader-slot-index.md | Complete | Shader reads `_InstanceData` from baked `TEXCOORD1.x` slot index. |
| 005-submission-path-rework.md | Complete | `CombatBatchedRenderSystem` drives mesh capacity/submesh active range; indirect args removed. |
| 006-remove-render-feature.md | Complete | Deleted old feature source/meta and removed feature entry from `Renderer2D.asset`. |
| 007-vfx-sorting-layer-authoring.md | Needs manual Unity authoring (revised approach) | VFX GameObjects are built fresh at runtime with no persistent object to author per-asset; fix is a `SortingGroup` on `CombatVfxRoot`'s GameObject, cascading to every child instance. |
| 008-docs-and-verification.md | Complete | Updated render docs/contracts for baked mesh + MeshRenderer path and recorded manual checks. |

## Completed Tasks
- 001-add-sorting-layers.md: `ProjectSettings/TagManager.asset` now lists `CombatVfx`, `CombatSprites`, `Default`.
- 002-baked-capacity-mesh.md: added non-shrinking capacity mesh builder and growth API.
- 003-persistent-mesh-renderer.md: added registry-owned renderer object and CombatRoot sorting configuration.
- 004-shader-slot-index.md: replaced `SV_InstanceID` with UV1 slot index lookup.
- 005-submission-path-rework.md: removed indirect args from batched render system and set active submesh range.
- 006-remove-render-feature.md: removed old render feature file and Renderer2D asset reference.
- 007-vfx-sorting-layer-authoring.md: runtime API attempt reverted; this must be verified/authored through Unity's VisualEffect Inspector serialization or another supported editor-time path.
- 008-docs-and-verification.md: updated render-system and render-batch docs.

## Blockers
- Unity manual visual verification still required: frame debugger one-draw check, overlap order check, growth test, idle-cost check.
- VFX draw order bug found during manual verification: VFX rendered between the combat sprite tier and player/mob (should sort below combat sprites) because VFX GameObjects have no persistent object to author a Sorting Layer on. Fix decided: `SortingGroup` on `CombatVfxRoot`'s GameObject (see revised 007). Needs re-verification after authoring.
- User decision (not in original plan): extend the `SortingGroup`-on-shared-parent proxy pattern to mobs and the player too, for authoring consistency across all four tiers. Player uses a `Players` Sorting Layer (group on `Player.prefab`'s root); mobs use a `Mobs` Sorting Layer, with the `SortingGroup` on a dedicated container assigned to `MobSpawnerRoot.spawnParent` (not on `MobSpawnerRoot`'s own GameObject — mobs aren't parented under the spawner script itself, so a group placed there was a no-op; fixed by introducing the container). Player and Mobs are **intentionally** separate Sorting Layers now — the original "must share one layer for Y-sort" invariant is superseded per user decision, see index.md.
- User decision (not in original plan): `CombatRenderResourceRegistry` no longer constructs the shared `Material` in code (`Shader.Find`/`new Material(...)` removed from `EnsureSharedResources`); it now reads `meshRenderer.sharedMaterial` in `AttachRenderer` and throws if unassigned. `Unregister()` no longer destroys/nulls it. **Requires new manual authoring**: create a Material asset with the `Combat/AtlasIndirectSprite` shader and assign it to the persistent `combatSpriteRenderer`'s Material slot in the Inspector before entering Play Mode, or combat sprites will throw `MissingReferenceException` on first registration. See [003](003-persistent-mesh-renderer.md)'s "Material authoring" update.

## Validation Summary
- Searched project for `m_SortingLayerID`; existing prefab/scene renderers were on Default (`0`).
- Searched `Assets`, `Docs`, and `ProjectSettings` for old indirect symbols; none remain.
- `dotnet build PlayGround.Runtime.csproj` could not validate because generated Unity project files still list the deleted source until Unity regenerates them.
- Full `dotnet build PlayGround.Runtime.csproj` also fails in Unity package generated code (`Unity.RenderPipelines.Core.Runtime.csproj`), outside this change.

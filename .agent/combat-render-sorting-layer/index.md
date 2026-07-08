# Combat Render Sorting Layer

## Summary

Replace the combat sprite batch's submission mechanism — currently one
`DrawMeshInstancedIndirect` issued from `CombatIndirectRenderFeature` (a custom
`ScriptableRendererFeature`, atomic relative to Renderer2D's own sprite pass) —
with one persistent `MeshRenderer`/`MeshFilter` holding a capacity-baked mesh
(N pre-built quads), submitted through Unity's normal `DrawRenderer2DPass` and
sorted via `sortingLayerID`/`sortingOrder` like any `SpriteRenderer`.

This lets the combat sprite batch, VFX (`VisualEffect`'s internal `VFXRenderer`),
and player/mob `SpriteRenderer`s all order against each other through one
mechanism (Sorting Layers) instead of two (RenderPassEvent atomicity +
Sorting Layer). Confirmed viable by reading URP 2D Renderer source directly
(see Constraints below) — not by running the Editor.

Target tier order, bottom to top: **CombatVfx → CombatSprites → Default
(player + mob, still Y-sorted against each other)**. Player and mob explicitly
stay on the same Sorting Layer per user decision — only VFX and the combat
sprite batch get pulled onto their own layers underneath that shared layer.

The atlas, static per-kind UV basis table, and per-instance `GraphicsBuffer`
(`_InstanceData`) are unchanged. Only the geometry-submission mechanism changes.

## Rationale

The custom `ScriptableRendererFeature` draw is invisible to Unity's per-object
sort key system (`Renderer.sortingLayerID`) no matter which `RenderPassEvent`
it's issued at — it's a raw command-buffer draw, not a `Renderer` component
that `DrawRenderer2DPass` discovers via culling. A `MeshRenderer` is a real
`Renderer`, so it is discovered and sorted the same way. Baking the whole
active pool into one persistent mesh keeps the "one draw call" property
without needing `DrawMeshInstancedIndirect`/indirect args at all.

## Constraints & Invariants

- **32-byte GPU/CPU struct parity** — `CombatBatchedRenderSystem.OnCreate`
  asserts `UnsafeUtility.SizeOf<CombatRenderComponent>() == 32`
  ([CombatBatchedRenderSystem.cs:36](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L36)).
  Unchanged by this plan — `CombatRenderComponent`/`CombatInstanceData` keep
  their exact shape.
- **`Material.SetBuffer`, not `MaterialPropertyBlock`, for structured buffers**
  ([combat-render-system.md:147-150](../../Docs/reference/simulation/combat-render-system.md#L147-L150)).
  This was documented as an indirect-draw-specific Unity quirk, but there is
  only ever one instance of the new `MeshRenderer`, so a shared `Material`
  bound the same way remains simplest — no reason to switch to MPB.
  `CombatBatchedRenderSystem.Submit` keeps calling
  `registry.SharedMaterial.SetBuffer(...)` unchanged.
  ([CombatBatchedRenderSystem.cs:149-154](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L149-L154))
- **`LightMode = Universal2D` required to draw at all under Renderer2D**
  ([combat-render-system.md:141-146](../../Docs/reference/simulation/combat-render-system.md#L141-L146)).
  Confirmed at the source level: `DrawRenderer2DPass` builds its `RendererList`
  against `ShaderTagId("Universal2D")`/`"SRPDefaultUnlit"`
  ([DrawRenderer2DPass.cs:15-19](../../Library/PackageCache/com.unity.render-pipelines.universal@13e5115b98bf/Runtime/2D/Rendergraph/DrawRenderer2DPass.cs#L15-L19)).
  The existing shader pass tag is reused unchanged.
- **Renderer-type-agnostic sorting** — `LayerUtility.GetFilterSettings` sets
  `filterSettings.sortingLayerRange = layerBatch.layerRange`
  ([LayerUtility.cs:184](../../Library/PackageCache/com.unity.render-pipelines.universal@13e5115b98bf/Runtime/2D/Passes/Utility/LayerUtility.cs#L184)),
  and `FilteringSettings.sortingLayerRange` filters on the base `Renderer.sortingLayerID`
  property, not a `SpriteRenderer`-specific one. This is the load-bearing fact
  the whole plan depends on (verified by source read, "test #1" in
  conversation) — a `MeshRenderer` is discovered and sorted identically.
- **`VisualEffect` exposes real `sortingLayerID`/`sortingOrder`** — confirmed
  via `VisualEffectEditor.cs`, which reads
  `m_SerializedRenderers.FindProperty("m_SortingLayerID"/"m_SortingOrder")`
  ([VisualEffectEditor.cs:1293-1298](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Inspector/VisualEffectEditor.cs#L1293-L1298)),
  the same serialized field names the base `Renderer` class uses ("test #2").
  VFX prefabs can therefore be placed on the `CombatVfx` layer directly.
- **Non-shrinking, grow-by-doubling pool** — `EnsureInstanceCapacity` never
  shrinks the instance `GraphicsBuffer`
  ([CombatBatchedRenderSystem.cs:119-134](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L119-L134)),
  consistent with [[project_idle_combat_system_cost]]'s non-shrinking-pool
  design. The new baked mesh must follow the same policy: grow the
  vertex/index buffers by doubling when active count exceeds capacity, never
  shrink them back down.
- **Zero Structural Churn** — render submission changes must not touch ECS
  archetypes or component shapes
  ([combat-render-system.md:175-184](../../Docs/reference/simulation/combat-render-system.md#L175-L184)).
  This plan only changes `CombatRenderResourceRegistry` (adds a mesh/GameObject
  it owns) and `CombatBatchedRenderSystem`'s submission tail; no ECS component
  changes.
- **Single atlas page / single shared material** — unchanged; the registry
  still owns exactly one `Material`/`SharedMaterial`.
- **`BoundsHalfExtent = 100000f`** — already defined on
  `CombatRenderResourceRegistry`
  ([CombatRenderComponents.cs:124](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L124))
  but currently unread/unused anywhere in the codebase. This plan gives it a
  real purpose: the baked mesh's `bounds` must be set large enough
  (`new Bounds(Vector3.zero, Vector3.one * BoundsHalfExtent)`) that Unity's
  camera-frustum culling never discards the renderer as active instances move,
  since per-quad bounds aren't tracked individually.
- **Player/mob Y-sort must be preserved** — confirmed with the user: player and
  mob currently share the one existing Sorting Layer (`Default`) with
  `TransparencySortMode = Default` and a Y sort axis
  ([Renderer2D.asset:34-35](../../Assets/Settings/Renderer2D.asset#L34)), so they
  interleave by vertical position today. They must **stay on the same Sorting
  Layer** — only VFX and the combat sprite batch move to new layers below it.

## Mechanisms Reused vs. Introduced

**Reused:**
- The entire scatter/upload pipeline in `CombatBatchedRenderSystem`
  (`WriteDirectJob`, `_instanceData` `NativeList`, `_instanceBuffer`
  `GraphicsBuffer`, `EnsureInstanceCapacity`'s doubling growth) — untouched.
- `CombatRenderResourceRegistry`'s atlas registration, UV basis computation,
  and `_uvBasisBuffer` — untouched.
- The existing unit-quad corner/UV layout (`-0.5..0.5` positions,
  `0..1` UVs) — reused verbatim, just repeated per baked quad instead of once.
- `BoundsHalfExtent` — an existing but dead constant, now used for real.
- The grow-by-doubling capacity pattern — applied a second time, to mesh
  vertex/index buffers, mirroring the instance buffer's existing policy.

**Introduced:**
- A capacity-baked `Mesh` (N quads) on `CombatRenderResourceRegistry`, replacing
  the 4-vertex unit quad.
- A per-vertex `slotIndex` stream (`TEXCOORD1`) so the shader can index
  `_InstanceData` without `SV_InstanceID` (which only exists for actual
  instanced/indirect draws, not a plain mesh submission).
- A persistent, scene/prefab-authored `MeshFilter`/`MeshRenderer` GameObject
  (not created at runtime) — its Sorting Layer/Order in Layer are set directly
  in its own Inspector and never touched by code. Referenced on `CombatRoot`
  via one serialized `MeshRenderer` field (`combatSpriteRenderer`), mirroring
  the existing `combatSpriteAtlas` field. (Revised after an initial runtime-
  created-GameObject-plus-string-field version caused a stale-serialized-field
  bug — see [003-persistent-mesh-renderer.md](003-persistent-mesh-renderer.md)'s
  "Status" note.)
- `Mesh.SetSubMesh(0, new SubMeshDescriptor(0, activeCount * 6), MeshUpdateFlags.DontRecalculateBounds)`
  per frame, replacing `GraphicsBuffer.IndirectDrawIndexedArgs`/`PopulateArgs`.
- Two new Sorting Layers in `ProjectSettings/TagManager.asset`: `CombatVfx`,
  `CombatSprites`, ordered below `Default`.

**Removed:**
- `CombatIndirectRenderFeature.cs` and `CombatIndirectRenderData` in full.
- The indirect args buffer (`_argsBuffer`), `PopulateArgs`, and the
  `SystemInfo.supportsIndirectArgumentsBuffer` guard in
  `CombatBatchedRenderSystem.OnCreate` — no longer relevant once nothing issues
  an indirect draw.
- The "Combat Indirect Render Feature" entry on `Renderer2D.asset` (manual
  Inspector removal).

## Design Validation

| Invariant | Held? | How |
|---|---|---|
| One draw call for the whole batch | Yes | One `MeshRenderer`, one mesh, one material, one submesh range — `DrawRendererList` cannot split a single renderer's own draw. |
| 32-byte struct parity | Yes | `CombatRenderComponent`/`CombatInstanceData` shapes untouched. |
| `Material.SetBuffer` requirement | Yes | Binding path in `CombatBatchedRenderSystem.Submit` unchanged. |
| `Universal2D` LightMode requirement | Yes | Shader pass tag unchanged; confirmed generically renderer-agnostic at the source level. |
| Zero structural churn (ECS) | Yes | Change is confined to `CombatRenderResourceRegistry` + `CombatBatchedRenderSystem`'s tail; no component/archetype changes. |
| Non-shrinking pool | Yes | Mesh capacity grows by doubling, mirrors existing instance-buffer policy; never rebuilt smaller. |
| Player/mob Y-sort preserved | Yes | Player and mob remain on `Default`; only VFX/combat-sprites move to new layers below it. |
| Atlas/UV basis pipeline unaffected | Yes | Registry's `Register`/`EnsureUvBasisBuffer` untouched. |

## Minimal/Additive vs. Refactor Comparison

**Additive approach** (keep `CombatIndirectRenderFeature`, layer ordering via
camera stacking instead): add Overlay Cameras with culling masks per tier
(vfx / ecs / player / mob), each rendering a GameObject-layer-filtered slice,
composited in camera-stack order.
- Resulting data flow: two independent ordering mechanisms — Sorting Layer for
  player/mob/VFX-within-a-camera, camera-stack order for the coarse tiers.
- New concepts/types introduced: N Overlay `Camera`s, GameObject culling-mask
  layers per tier, camera-stack configuration.
- Copies/translations added: each stacked camera re-renders/re-culls the scene
  slice assigned to it — extra per-frame render overhead per tier.
- Long-term cost: two parallel ordering systems that must be kept in sync by
  hand (Sorting Layer changes for depth *within* a tier, camera stack order
  for depth *between* tiers); adding a fifth tier means adding another camera.

**Refactor approach (chosen):** replace the indirect-draw/RendererFeature path
with a baked mesh through a real `MeshRenderer`, unifying all four tiers onto
one ordering mechanism (Sorting Layer).
- Resulting data flow: unchanged scatter/upload pipeline; submission tail
  writes to a persistent mesh + `MeshRenderer` instead of an indirect args
  buffer + custom render pass.
- Existing concepts/types changed or removed: `CombatIndirectRenderFeature`/
  `CombatIndirectRenderData` deleted; `CombatRenderResourceRegistry` gains a
  baked mesh + owned GameObject instead of a 4-vertex mesh.
- Copies/translations removed: no more indirect-args bookkeeping; no extra
  cameras or culling-mask bookkeeping.
- Long-term benefit: one ordering mechanism for every tier (VFX, combat
  sprites, player, mob) — adding a fifth tier is "add a Sorting Layer," not
  "add a camera." Net code deletion (whole RendererFeature file gone).

**Decision: refactor.** Per the plan-changes default rule, ordering is one
domain concept currently expressed two ways (RenderPassEvent atomicity vs.
Sorting Layer); collapsing to one representation removes a structural warning
(two parallel systems that must stay in sync) rather than adding a second one.

## Task List

1. [001-add-sorting-layers.md](001-add-sorting-layers.md) — add `CombatVfx` and
   `CombatSprites` Sorting Layers to the project, ordered below `Default`.
2. [002-baked-capacity-mesh.md](002-baked-capacity-mesh.md) — replace the unit
   quad mesh with a capacity-baked mesh + grow-by-doubling.
3. [003-persistent-mesh-renderer.md](003-persistent-mesh-renderer.md) — owned
   `MeshFilter`/`MeshRenderer` GameObject + `CombatRoot` sorting fields.
4. [004-shader-slot-index.md](004-shader-slot-index.md) — shader: `SV_InstanceID`
   → per-vertex `slotIndex`.
5. [005-submission-path-rework.md](005-submission-path-rework.md) —
   `CombatBatchedRenderSystem`: drop indirect args, add `SetSubMesh` per frame.
6. [006-remove-render-feature.md](006-remove-render-feature.md) — delete the
   old RendererFeature/handoff, remove it from `Renderer2D.asset`.
7. [007-vfx-sorting-layer-authoring.md](007-vfx-sorting-layer-authoring.md) —
   assign `CombatVfx` layer to registered VFX prefab renderers.
8. [008-docs-and-verification.md](008-docs-and-verification.md) — update docs,
   PlayMode/manual verification checklist.

Dependencies: 002 → 003 → 004 → 005 (sequential, each depends on the previous
producing the mesh/GameObject/shader/system it touches). 001 and 007 have no
code dependency on the others and can happen anytime. 006 depends on 005
being live (nothing must still publish to `CombatIndirectRenderData`). 008 is
last.

## Post-Implementation Decision: SortingGroup-as-proxy, applied project-wide

Discovered during manual verification: assigning a Sorting Layer works more
reliably as a `SortingGroup` on a shared **parent** than as per-renderer
Inspector fields, because two of the four tiers don't have a stable
per-instance object to author on:

- Combat sprites: one persistent `MeshRenderer`, but a `SortingGroup` wrapping
  it (rather than its own `sortingLayerID`/`sortingOrder`) was used in
  practice — see [003](003-persistent-mesh-renderer.md)'s "SortingGroup
  proxy" update.
- VFX: no persistent object at all — every `VisualEffect` is built fresh at
  runtime via `AddComponent`. A `SortingGroup` on `CombatVfxRoot`'s GameObject
  cascades to every child VFX instance automatically — see the revised
  [007](007-vfx-sorting-layer-authoring.md).
- Mobs: also built fresh at runtime (`MobSpawnerRoot.RequestSpawn` →
  `Instantiate(prefab, ..., parent)` where `parent` is a shared `spawnParent`
  Transform, [MobSpawnerRoot.cs:138-139](../../Assets/Scripts/Spawn/MobSpawnerRoot.cs#L138-L139)).
  A `SortingGroup` on that shared `spawnParent` covers every spawned mob the
  same way, by user decision.
- Player: a single scene object; a `SortingGroup` on its GameObject (or
  whatever parent holds its visual) applied for consistency with the other
  three tiers, by user decision.

**Load-bearing correctness requirement:** the player-tier `SortingGroup` and
the mob-tier `SortingGroup` must be set to the exact same Sorting Layer
(`Default`) **and** the exact same Order in Layer. A `SortingGroup` fully
overrides its members' effective `sortingLayerID`/`sortingOrder`; the Y-axis
tie-break (`TransparencySortMode.CustomAxis`,
[Renderer2D.asset:34-35](../../Assets/Settings/Renderer2D.asset#L34)) only
applies to *members with an identical (layer, order) tuple*. If the two
groups end up with different Order in Layer values, every mob will draw
either fully in front of or fully behind every player regardless of Y
position, silently breaking the "player/mob Y-sort must be preserved"
invariant this plan originally locked in. This must be checked visually after
authoring (mob passing behind/in front of player at multiple relative Y
positions), not assumed from the Inspector values alone.

One remaining code-level snag found while tracing this: `MobSpawnerRoot`'s
debug/fallback path (`CreateRuntimeMobPrefab`, used only when no
`MobSpawnPool` is assigned) hard-codes
`renderer.sortingOrder = 9`
([MobSpawnerRoot.cs:269](../../Assets/Scripts/Spawn/MobSpawnerRoot.cs#L269)).
Once a `SortingGroup` owns that mob's GameObject, this per-renderer value is
ignored (the group's Order in Layer wins) — harmless as dead code, but worth
knowing so it isn't mistaken for a live knob if debugging order issues later.

## Open Questions

- Exact `sortingOrder` values within `CombatVfx`/`CombatSprites` (single value
  each is sufficient since each layer holds one renderer) — default to `0`
  unless another system already claims that layer's order space.
- Whether any other current `SpriteRenderer`/`MeshRenderer` already uses
  `Default` with a nonzero `sortingOrder` that would need to be renumbered
  once `CombatVfx`/`CombatSprites` exist below it — not found in code search,
  but worth a quick project-wide check before merging 001.

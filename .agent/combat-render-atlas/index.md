# Combat Render Atlas

## Revision Note (2026-07-03)

This plan was revised after initial implementation per explicit user direction. Four changes
from the original design, carried through this whole document and the task files:

1. **Single-page atlas, configurable via serialized field** — not multi-page, not runtime-grown.
2. **No dynamic atlas generation.** The atlas texture is manually assembled in the Unity Editor
   (an artist/developer slices multiple `Sprite`s from one shared texture asset ahead of time).
   `Register(...)` computes each sprite's UV rect directly from `sprite.rect` /
   `AtlasTexture.width/height` — there is no runtime packing step at all.
3. **Skills register themselves against the configured atlas; if the sprite isn't part of it,
   `Register(...)` throws.** Fail loud on a misconfigured/mismatched sprite rather than silently
   doing something wrong.
4. **UV rect is stored on the entity (in `CombatRenderComponent`), computed once when the spawn
   command is built — not looked up from a registry table every frame in the render scatter
   loop.**

The net effect is a **simpler** design than the original: no packer, no dirty-flag/rebuild cycle,
no per-frame dictionary lookup. `CombatSpriteAtlasPacker` (originally task 002) is deleted
entirely; task 002 is repurposed to "Atlas Configuration." Everything below reflects the current,
implemented design.

## Summary

Replace one-draw-call-per-registered-sprite-kind rendering
(`CombatBatchedRenderSystem` + `Graphics.RenderMeshInstanced`, one `Mesh` + one
`Material` per kind) with a shared-atlas approach: every registered sprite kind is a slice of one
manually-assembled atlas texture, drawn with one shared unit-quad mesh + one shared hand-written
instanced shader that reads a per-instance UV rect (stored on the entity, not looked up per
frame). Draw call count drops from "one per kind" to "one per 1023 active instances" (the
existing `Graphics.RenderMeshInstanced` cap, unrelated to atlassing).

## Rationale

1. **Atlas + shared shader, not Entities Graphics.** A prior attempt used
   Entities Graphics/BatchRendererGroup for the same draw-call-reduction goal;
   it was fully implemented then abandoned because this project's URP renderer
   is the 2D Renderer, which `com.unity.entities.graphics` has zero
   integration with (confirmed via package source + docs — nothing renders,
   `EntitiesGraphicsSystem` runs but never submits). `Graphics.RenderMeshInstanced`
   is SRP-agnostic and already proven working on this renderer, so the atlas
   approach reuses that submission path rather than a renderer-incompatible one.
2. **Hand-written HLSL/ShaderLab shader, not Shader Graph** (user-confirmed).
   The per-instance-UV-remap pattern via `UNITY_DEFINE_INSTANCED_PROP` is
   standard for hand-written instanced shaders; more reviewable/diffable than
   a node graph in this text-based workflow, and there is no existing Shader
   Graph asset in the project to extend from (no custom shaders exist at all
   today besides TextMesh Pro's).
3. **One shared atlas + one shared material for everything, not per-material
   grouping** (user-confirmed). `CombatRenderResourceRegistry.ConfigureMaterial`
   (pre-rework code) already forced every cloned material into the same
   transparent/unlit blend state regardless of the source shader passed in —
   there is no real per-kind shader diversity in the current system to
   preserve, so grouping would add complexity for a distinction that doesn't
   functionally exist today.
4. **Manually-assembled single-page atlas, not runtime packing** (user-directed
   revision). A hand-authored atlas is simpler and more predictable than a
   runtime packer: no packing algorithm to maintain, no padding/bleed
   concerns to get right in code, no repack cost, and an artist gets full
   control over layout. The trade-off — every new sprite requires a manual
   editor step (slice it into the shared atlas texture) before it can be
   registered — is accepted; `Register(...)` enforces it by throwing if a
   sprite isn't part of the configured atlas texture, so a missed step fails
   immediately and loudly instead of silently misrendering.
5. **UV rect stored per-entity, computed once per spawn.** `CombatRenderComponent`
   is already computed once when a spawn command is built
   (`CombatRoot.ProjectileCommandFor`/`AoeCommandFor` →
   `GetProjectileRenderComponent`/`GetAoeRenderComponent`) and already flows
   through existing apply-system plumbing (cold-create ECB, hot-reuse IJob)
   onto the entity unchanged. Adding `UvRect` as a field on that struct reuses
   this existing "compute once, carry on the entity" path for free — no new
   component, no apply-system changes, and the presentation-side scatter loop
   becomes a flat per-entity read instead of a dictionary lookup.

## Constraints And Invariants

- **Zero structural churn on spawn/despawn/reuse.** Pool reuse/despawn only
  ever flips enableable `CombatRenderActiveTag` and rewrites value components;
  never adds/removes components or destroys/recreates entities. Source:
  `ProjectileSpawnApplySystem.cs`/`AoeSpawnApplySystem.cs` reuse-IJob +
  cold-create-ECB paths. This plan must not add any per-despawn/reuse
  structural op — and doesn't, since `UvRect` is just one more field on the
  already-existing `CombatRenderComponent`, copied the same way every other
  field on it already is.
- **`CombatRenderPrepareSystem` stays active-only.** Its `RenderPrepareJob`
  only writes `CombatRenderElement` for entities with `CombatRenderActiveTag`
  enabled. Source: `CombatRenderPrepareSystem.cs`, current code — unchanged by
  this plan; the prepare system is not touched at all.
- **`Graphics.RenderMeshInstanced` hard cap of 1023 instances per call.**
  Source: `CombatBatchedRenderSystem.cs`, `MaxInstancesPerDraw` — an engine
  API limit, independent of atlassing; the submit loop must keep chunking at
  this boundary.
- **Domain/faction identity must not move onto render data.** Source:
  `Docs/contracts/render-batch-data.md` "Restrictions" — domain comes from
  `ProjectileTag`/`AoeTag`, faction from `CombatFaction`, never render state.
  Unaffected by this plan.
- **Spawn pooling must not key on `CombatRenderBatchId`.** Source:
  `render-batch-data.md` "Restrictions" — reuse can claim any disabled slot
  and must overwrite the batch id from the current spawn command. Unaffected;
  `CombatRenderBatchId.Value = cmd.RenderTypeId` assignment in apply systems
  is unchanged (the field is now a plain kind identifier, no longer a lookup
  key for anything render-related).
- **Registry lifetime is owned by `CombatRoot`.** `CombatRenderResourceRegistry.Unregister()`
  is called from `CombatRoot.OnDestroy` and must destroy the GPU resources it
  *created* (shared `Mesh`/`Material`). Source: `CombatRoot.cs`. It must
  **not** destroy `AtlasTexture` — that's a manually-assigned project asset
  the registry does not own, only references.
- **The atlas texture is a single-page, manually-assigned asset, not
  generated or grown at runtime.** Exposed as `[SerializeField] private
  Texture2D combatAtlasTexture` on `CombatRoot`, threaded into the registry
  via `ConfigureAtlas(...)`, called once in `CombatRoot.Awake()` before any
  `Register(...)` call.
- **`Register(...)` must throw, not silently degrade, if the atlas isn't
  configured or the sprite isn't part of it.** (User-directed.) Checked via
  `sprite.texture != AtlasTexture` reference comparison — this only works
  correctly if the sprite was actually sliced from the configured atlas
  texture asset (not an independent texture that merely looks similar).
- **Render kinds can still be registered after combat entities already
  exist.** Confirmed via `PlayerSkillDriver.RegisterProjectileTypes`/`RegisterAoeTypes`
  → `CombatRoot.RegisterTemplate`/`RegisterType`/`RegisterConfig` →
  `CombatRenderResourceRegistry.Register`, invoked from a loadout-recompile
  path that can run mid-run. This remains safe under the new design without
  any special handling: each `Register(...)` call computes its own UV rect
  independently from its own sprite's `rect`, with no shared/batched state
  to invalidate — simpler than the original runtime-packer design, which
  needed an explicit repack-on-growth story.
- **AOE visual scale comes from `geometry.VisualScale` alone unless folded
  with the registry entry's own scale.** Source: `GetAoeRenderComponent` +
  `AoeTypeRegistry.cs` `TryBakeVisual` (hardcodes registered `VisualScale` to
  `Vector2.one`) — the sprite's real pixel size must come from the registry
  entry, since the shared unit-quad mesh doesn't bake it into vertices. Fixed
  in task 004, independent of the atlas-configuration revision.

## Reused Vs Introduced

Reused:
- `CombatRenderBatchId` (int) — still copied from `cmd.RenderTypeId` in every
  apply path unchanged; now purely a kind identifier (no longer a lookup key
  for anything render-related, since `UvRect` lives directly on
  `CombatRenderComponent`).
- `CombatRenderActiveTag`, `CombatRenderElement` — unchanged shapes;
  `CombatRenderPrepareSystem`'s active-only matrix-write pattern is untouched.
- `CombatRenderComponent` — extended (one new `float4 UvRect` field), not
  replaced; all existing plumbing that copies it onto entities (cold-create
  ECB, hot-reuse IJob) picks up the new field automatically.
- `Graphics.RenderMeshInstanced` submission via `RenderParams`, chunked at
  `MaxInstancesPerDraw` — same API, same chunking loop shape.
- `CombatRoot.Register(...)`/`Unregister()` call pattern and lifetime —
  callers in `CombatRoot.cs` are unaffected (same 4-argument signature); only
  the registry's internals change.
- `CombatRoot.Configure(Sprite sprite)`'s pattern of a public setter for
  pre-`Awake` test configuration — mirrored by the new
  `CombatRoot.ConfigureAtlas(Texture2D atlas)`.
- `MaterialPropertyBlock` per-instance data delivery via `SetVectorArray` for
  the UV rect array — unchanged from the prior revision of this plan.

Introduced:
- `[SerializeField] private Texture2D combatAtlasTexture` on `CombatRoot`
  (single page, manually assigned).
- `CombatRenderResourceRegistry.ConfigureAtlas(Texture2D)` — assigns the
  atlas texture and the shared material's `mainTexture`.
- `CombatRenderComponent.UvRect` (`float4`) — computed once per registered
  kind, copied onto every spawned entity via existing `CombatRenderComponent`
  plumbing.
- One shared unit-quad `Mesh` and one shared `Material`
  (`CombatAtlasInstancedSprite.shader`, hand-written shader) replacing the
  per-kind `Mesh`/`Material`/`MaterialPropertyBlock` triple — unchanged from
  the prior revision.

Removed (relative to the pre-atlas baseline):
- Per-kind `Mesh`/`Material`/`MaterialPropertyBlock` construction in
  `CombatRenderResourceRegistry`.
- Per-kind `Dictionary<int, NativeList<Matrix4x4>>` batch buffers in
  `CombatBatchedRenderSystem`.

Removed (relative to the first implementation of *this* plan, during the
2026-07-03 revision):
- `CombatSpriteAtlasPacker` (shelf packer) — deleted entirely, no runtime
  packing.
- `CombatRenderResourceRegistry.UvRectsByRenderId`/`EnsureAtlasCurrent()`/`_atlasDirty` —
  deleted; no per-frame lookup, no dirty-flag rebuild cycle.

## Design Validation

- **No structural churn:** `UvRect` is one more field on the already-existing
  `CombatRenderComponent`; no archetype, no ECB structural op, no new
  component types on entities. PASS.
- **Prepare stays active-only:** `CombatRenderPrepareSystem` is not modified
  by this plan at all. PASS (trivially).
- **1023-instance cap:** submit loop keeps the existing `for (start +=
  MaxInstancesPerDraw)` chunking shape, slicing both the transform array and
  the UV-rect array (now read from `CombatRenderComponent` per entity, not a
  registry table) together. PASS.
- **Domain/faction identity:** no domain or faction data is read or written
  anywhere in the registry/render-system code. PASS.
- **Pooling never keys on batch id (structurally):** unchanged apply-path
  behavior (verified: both hot-reuse IJob and cold-create ECB paths in
  `ProjectileSpawnApplySystem.cs`/`AoeSpawnApplySystem.cs` just copy
  `cmd.RenderTypeId` into `CombatRenderBatchId.Value`, no other logic keys off
  it). PASS.
- **Registry teardown:** `Unregister()` destroys the shared `Mesh`/`Material`
  it created; it does *not* destroy `AtlasTexture` (a referenced, not owned,
  asset). PASS.
- **Single-page, manually-assembled atlas:** `Register(...)` throws instead
  of silently packing or misrendering if the atlas isn't configured or the
  sprite doesn't belong to it. PASS.
- **Runtime kind growth:** each `Register(...)` call computes its own UV rect
  independently; no shared packed state to invalidate, no repack. PASS —
  simpler than the prior revision's repack-on-growth story.
- **AOE visual scale:** `GetAoeRenderComponent` multiplies `geometry.VisualScale
  * entry.VisualScale` (task 004) so AOEs render at the correct size now that
  native sprite pixel size isn't baked into mesh vertices. PASS.

## Additive Vs Refactor

This section is preserved from the plan's original (pre-revision) reasoning —
the atlas-vs-per-kind decision itself is unchanged by the 2026-07-03
revision, only *how* the atlas is produced (manual vs. runtime-packed) and
*how* UV rects reach the GPU (per-entity field vs. per-frame table lookup)
changed. Both of those are refinements within the "refactor" branch below,
not a re-litigation of it.

Minimal/additive approach (keep per-kind meshes/materials/dictionary; only add
a shared atlas *texture* that every per-kind material samples from, each kind
keeping its own baked-UV mesh and its own `Material`/`MaterialPropertyBlock`
entry in `CombatBatchedRenderSystem`'s per-kind dictionary):

- resulting data flow: unchanged shape — `CombatRenderComponent` → prepare →
  `CombatRenderElement` → per-kind dictionary scatter → per-kind
  `RenderMeshInstanced` submit, just now all kinds' materials point at
  sub-rects of one shared texture instead of per-kind textures.
- new concepts/types introduced: an atlas texture, bolted on beside the
  existing per-kind mesh/material/dictionary machinery (a second
  representation of "what to draw" living alongside the first).
- copies/translations added: none removed; the per-kind `Dictionary`,
  per-kind `Mesh`, and per-kind `Material` all remain.
- long-term cost: two parallel "what to draw" representations for no
  draw-call benefit — `Graphics.RenderMeshInstanced` batches by (mesh,
  material) pair, and since each kind still has its own `Mesh`/`Material`,
  draw call count stays at one per kind, identical to today.

Refactor approach (this plan — one shared mesh, one shared material/shader,
one shared manually-assembled atlas, UV rect stored per-entity):

- resulting data flow: `CombatRenderComponent` (now carrying `UvRect`) →
  prepare (unchanged) → one transform buffer + one parallel UV-rect buffer,
  both filled by reading each active entity's own components directly (no
  registry lookup) → one (or few, 1023-capped) `RenderMeshInstanced` submit.
- existing concepts/types changed or removed: per-kind
  `Dictionary<int, NativeList<Matrix4x4>>` removed; per-kind
  `Mesh`/`Material`/`MaterialPropertyBlock` triple removed, replaced by one
  shared instance of each; sprite pixel size moves from mesh vertices to
  `CombatRenderResourceEntry.VisualScale`.
- copies/translations removed or avoided: the "per-kind everything"
  indirection disappears entirely; there is no packer, no dirty-flag/rebuild
  cycle, and no per-frame registry-table lookup — UV rect is computed exactly
  once (at `Register(...)` time) and carried on the entity like every other
  render input.
- long-term benefit: one render data path, actually achieves the draw-call
  reduction goal, no runtime packing algorithm to maintain, no per-frame
  lookup cost, fewer GPU resources to manage.

Decision:

- choose refactor.
- reason: unchanged from the original decision — the additive path cannot
  reduce draw calls without unifying the mesh across kinds. The 2026-07-03
  revision (manual atlas, per-entity UV rect) makes the refactor branch
  strictly simpler than originally planned, reinforcing the same choice.

`CombatRenderBatchId` (a render-kind selector) and the render-kind's actual
GPU representation (mesh region + material) are the same domain concept
before and after this change — only *how* that representation is stored
changes. Per the default decision rule, `UvRect` collapses to one
representation (computed once, stored on the entity) rather than a second,
per-frame-refreshed lookup table living alongside it.

## Tasks

- [001-atlas-shader.md](001-atlas-shader.md)
- [002-atlas-configuration.md](002-atlas-configuration.md) — was "atlas
  packer"; repurposed in the 2026-07-03 revision to describe the manually-
  assembled atlas + `ConfigureAtlas`/serialized-field/validation design.
  `CombatSpriteAtlasPacker` (the file this task originally produced) is
  deleted.
- [003-registry-rework.md](003-registry-rework.md)
- [004-visual-scale-folding.md](004-visual-scale-folding.md)
- [005-batched-render-system-rework.md](005-batched-render-system-rework.md)
- [006-tests.md](006-tests.md)
- [007-docs.md](007-docs.md)

Dependencies: 001 and 002 are independent (parallelizable). 003 depends on
001 and 002. 004 and 005 both depend on 003 and can proceed in parallel with
each other. 006 depends on 003/004/005. 007 depends on all prior tasks.

## Open Questions

1. ~~Atlas sizing/multi-page fallback~~ — resolved by the 2026-07-03 revision:
   single page, manually assembled, no runtime sizing decision to make in
   code.
2. **Vestigial `.mat` assets:** `Assets/Material/InstancedSprite*.mat` (used
   previously as optional `sourceMaterial` overrides passed into `Register()`)
   are unused now that every kind shares one atlas material. Not a task in
   this plan (asset deletion is a manual editor action, out of scope for an
   `.agent/`-only pass) — flagged here so it isn't forgotten as a follow-up.
3. **Content-authoring follow-up (new, 2026-07-03, not resolvable in code):**
   no combat-atlas texture asset exists yet, and no skill sprite has been
   re-sliced from one. Until that content work happens, `Register(...)` will
   throw for every real skill registration in actual gameplay scenes (as
   designed — fail loud). The one scene with a live `CombatRoot` instance
   (`Assets/Scenes/BenchmarkLarge.unity`) will need its new `Combat Atlas
   Texture` field assigned once the atlas asset exists. This is squarely a
   content task (create the atlas texture, slice sprites, reassign every
   skill prefab's `SpriteRenderer.sprite` to the sliced versions, assign the
   atlas asset in the Inspector) that needs the Unity Editor and cannot be
   done by editing files blindly.

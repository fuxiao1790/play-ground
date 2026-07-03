# Implementation Log

## Status
Complete, including two 2026-07-03 revisions (see below): the first replaced runtime atlas packing
with a manually-assembled single-page atlas and moved UV rect storage from a per-frame registry
lookup onto the entity; the second corrected the atlas field from a plain `Texture2D` to a real
`UnityEngine.U2D.SpriteAtlas` asset per user feedback. No live Unity Editor compile/test run
performed in this session (no Editor available in this environment) — recommend a domain reload +
manual Play Mode check + PlayMode Test Runner pass before considering this done. **Real gameplay
use additionally needs content work outside this session's reach, and the PlayMode suite needs two
new test asset files the user is creating — see "Revision (2026-07-03)" and "Revision 2
(2026-07-03)" below.**

## Task Progress (current, post-revision)

| Task | Status | Notes |
|---|---|---|
| 001-atlas-shader.md | Complete | `Assets/Shaders/CombatAtlasInstancedSprite.shader`. Unaffected by the revision. |
| 002-atlas-configuration.md (was atlas-packer) | Complete (redesigned) | Packer deleted; `CombatRoot` serialized field + `ConfigureAtlas` + `Register(...)` validation. |
| 003-registry-rework.md | Complete (redesigned) | Registry now computes UV rect immediately at `Register()`, no dirty-flag/rebuild cycle. |
| 004-visual-scale-folding.md | Complete | Unaffected by the revision — orthogonal to `UvRect`/atlas mechanism. |
| 005-batched-render-system-rework.md | Complete (redesigned) | Scatter reads `CombatRenderComponent.UvRect` directly; no registry lookup in the hot loop. |
| 006-tests.md | Complete (extended) | Original fix + new atlas-configuration requirement across 6 fixture helpers. |
| 007-docs.md | Complete | `render-batch-data.md` rewritten sections; 4 secondary docs given small targeted edits. |

## Revision (2026-07-03)

User gave four explicit requirements after the initial implementation, requiring real design
changes (not just terminology): single-page atlas configurable via serialized field; no dynamic
atlas generation (manually assembled in editor); skills register themselves, throw if the sprite
isn't in the atlas; store UV rect on entities, computed once per spawn (not looked up per frame).
Updated `index.md` and tasks 002/003/005/006/007 to match (002 renamed/repurposed from
"atlas-packer" to "atlas-configuration"; old `002-atlas-packer.md` deleted).

Code changes:
- **Deleted** `Assets/Scripts/System/Common/CombatSpriteAtlasPacker.cs` entirely — no runtime
  packing algorithm anymore.
- **`CombatRenderComponent`** gained a `float4 UvRect` field. This reuses the *existing*
  "computed once at spawn-command-build time, copied onto the entity via already-existing
  apply-system plumbing" path that `VisualScale`/rotation/etc. already used — no apply-system
  code changes were needed for this (`ecb.SetComponent(entity, cmd.Render)` /
  `renders[i] = cfg.Render;` already copy the whole struct).
- **`CombatRenderResourceRegistry`**: removed `UvRectsByRenderId`/`EnsureAtlasCurrent()`/`_atlasDirty`
  (no per-frame table, no rebuild cycle). Added `AtlasTexture` (referenced, not owned — `Unregister()`
  must not `Destroy()` it, unlike the registry-created `SharedMesh`/`SharedMaterial`) and
  `ConfigureAtlas(Texture2D)`. `Register(...)` now computes `UvRect` immediately from
  `sprite.rect`/`AtlasTexture.width`/`height` and throws (`InvalidOperationException`, not a
  silent fallback) if the atlas isn't configured yet or `sprite.texture != AtlasTexture`.
  `CombatRenderResourceEntry` dropped the `Sprite` field (no longer needed post-registration since
  nothing repacks later) and gained `UvRect`.
- **`CombatRoot`**: added `[SerializeField] private Texture2D combatAtlasTexture` (new "Render
  Atlas" header, above "Projectile visuals") and `public void ConfigureAtlas(Texture2D)`
  (mirrors the existing `Configure(Sprite)` pre-`Awake` test-hook pattern). `Awake()` calls
  `_renderRegistry?.ConfigureAtlas(combatAtlasTexture)` before `BuildProjectileRenderResources()`.
- **`CombatBatchedRenderSystem`**: `Scatter` now fetches `ComponentTypeHandle<CombatRenderComponent>`
  (read-only) instead of `ComponentTypeHandle<CombatRenderBatchId>`, and reads `renders[i].UvRect`
  directly — no `registry` parameter needed by `Scatter` anymore, no dictionary lookup. The old
  "dictionary-miss = skip" filter is replaced by an explicit `renders[i].IsRenderable == 0` check
  (already the correct signal for "this entity has no visual," previously redundant with the
  lookup miss).

**Real, non-obvious consequence found and handled: test fixtures.** `Register(...)`'s new
`sprite.texture != AtlasTexture` check would throw for every PlayMode test that creates a real
`CombatRoot` and registers a real sprite, since `combatAtlasTexture` defaults to null on a
freshly-`AddComponent`'d `CombatRoot` (no serialized asset data to deserialize into a
programmatically-created instance). Found and fixed **6** fixture helpers across
`AoePlayModeTests.cs` and `BareMinimumPrototypePlayModeTests.cs`, adding
`root.ConfigureAtlas(Texture2D.whiteTexture)` before `SetActive(true)` (the same pre-`Awake`
window `Configure(Sprite)` already used) — chosen because every synthetic test sprite in both
files already uses `Sprite.Create(Texture2D.whiteTexture, ...)` consistently, so this satisfies
the reference-equality check uniformly. `AoeSimulationTests.cs`/`ProjectileCollisionSimulationTests.cs`/`ProjectileSpawnPipelineTests.cs`
never construct a real `CombatRoot` (bare `World` + hand-built entities), so unaffected.

**Not resolvable in code — flagged, not attempted:** no real combat-atlas texture asset exists in
the project yet, and no skill sprite has been re-sliced from one. Until that content work happens
(create the atlas texture, slice sprites, reassign every skill prefab's `SpriteRenderer.sprite`,
assign the atlas in the Inspector), `Register(...)` will throw for real skill registration in
actual gameplay — by design (fail loud), but it means gameplay won't run until this content work
is done. The one scene with a live `CombatRoot` (`Assets/Scenes/BenchmarkLarge.unity`) will need
its new `Combat Atlas Texture` field assigned once that asset exists. This is a Unity Editor /
image-authoring task, not something achievable by editing files blindly.

## Revision 2 (2026-07-03, same day): Texture2D → Real SpriteAtlas

*(This revision's `Register(...)` design assumed a `SpriteAtlas.GetTexture()` method that does not
actually exist — the user hit a real `CS1061` compiler error. See "Revision 3" below for the fix;
narrative below is left as originally written for the historical record of what was attempted, but
any mention of `GetTexture()` in it describes code that never compiled.)*

User caught a mistake in the first revision immediately via IDE selection feedback: *"this field
does not accept sprite atlas type."* The `combatAtlasTexture` field was a plain `Texture2D`
(sprites manually sliced from one big texture via Sprite Editor "Multiple" mode); the user's
actual intent was Unity's real `UnityEngine.U2D.SpriteAtlas` asset type — the same kind of asset
as the project's pre-existing `Assets/Atlas/Skills.spriteatlasv2` (confirmed by reading that file:
it's a genuine `SpriteAtlasAsset` YAML, packables referenced by GUID+fileID, distinct from a
sliced-texture approach).

Code changes:
- `CombatRoot.cs`: `combatAtlasTexture` (`Texture2D`) → `combatSpriteAtlas` (`SpriteAtlas`);
  `using UnityEngine.U2D;` added; `ConfigureAtlas(Texture2D)` → `ConfigureAtlas(SpriteAtlas)`.
- `CombatRenderComponents.cs`: registry gained `Atlas` (`SpriteAtlas`) property. `AtlasTexture`
  (`Texture2D`) is no longer set eagerly in `ConfigureAtlas(...)` (a fresh/unpacked `SpriteAtlas`
  can return null from `GetTexture()`) — it's resolved fresh inside `Register(...)` every call.
  `Register(...)` gained a distinct third throw case: atlas configured but `GetTexture()` still
  null (not packed yet), alongside the existing "not configured" and "sprite not part of atlas"
  cases. The UV-rect math itself is **unchanged** — `sprite.rect`/atlas-texture-dimensions still
  works because Unity's Sprite Packing system transparently redirects a packed sprite's
  `.texture`/`.rect` to the atlas's page; only *how the texture reference is obtained* changed.

Design decision explicitly asked and answered via `AskUserQuestion`: user confirmed "switch to
`UnityEngine.U2D.SpriteAtlas`" over "keep `Texture2D`, slice via Sprite Editor" — the field was
always meant to hold a real atlas asset, not a big sliced texture.

**Test fixtures needed a deeper fix than revision 1's.** A real `SpriteAtlas` only validates
sprites actually packed into it by Unity's real asset pipeline (texture import + atlas packable
list + Pack Preview/build-time pack) — the in-memory `Sprite.Create(Texture2D.whiteTexture, new
Rect(0f,0f,1f,1f), Vector2.one * 0.5f)` pattern every fixture used cannot satisfy this; there is no
supported pure-runtime way to fabricate a packed `SpriteAtlas` from C# alone (confirmed: packing
is an Editor-asset-pipeline operation, and `SpriteAtlas` packables must be persisted, GUID-backed
assets, not synthetic in-memory objects).

Asked the user two follow-up questions rather than guessing: (1) how to bridge tests — user chose
**"I'll create a real SpriteAtlas test asset myself"** (not a test-only code bypass, not leaving
tests broken); (2) where that asset should live — user chose
`Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2`.

Implemented in anticipation of that asset:
- **New** `Assets/Tests/PlayMode/CombatAtlasTestFixture.cs` (`#if UNITY_EDITOR`-guarded): loads
  `Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2` via `AssetDatabase.LoadAssetAtPath` and
  the one `Sprite` sub-asset of `Assets/Tests/TestAssets/CombatAtlasTestSource.png` via
  `AssetDatabase.LoadAllAssetsAtPath` + `OfType<Sprite>()`, both cached in static fields. Loads the
  *original* asset-database sprite reference (not `SpriteAtlas.GetSprite(name)`) deliberately —
  this matches how production code actually receives sprites (a skill prefab's authored `Sprite`
  field, `.texture`-redirected post-pack), so the test path exercises the same mechanism real
  skills do.
- Every `Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f)` call
  site (6 in `AoePlayModeTests.cs`, 3 in `BareMinimumPrototypePlayModeTests.cs` — confirmed via
  grep, all nine used the *identical* signature) replaced with `CombatAtlasTestFixture.Sprite`.
- Every `ConfigureAtlas(Texture2D.whiteTexture)` call site (6 total, matching the fixture count
  from revision 1) replaced with `ConfigureAtlas(CombatAtlasTestFixture.Atlas)`.
- Verified via grep afterward: zero remaining `Texture2D.whiteTexture`/`Sprite.Create` references
  in `Assets/Tests/PlayMode/` outside an explanatory comment.

**Not yet done — blocking, user-owned:** the two real asset files
(`Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2`,
`Assets/Tests/TestAssets/CombatAtlasTestSource.png`) do not exist in the repo yet. Until the user
creates them (spec recorded in task 006: 1×1px sprite region, pivot (0.5, 0.5), pixels-per-unit
100 — matching the old synthetic sprite's properties exactly, so every hand-derived assertion like
task 004's `(0.06, 0.08)` AOE matrix-scale values stays correct with no re-derivation),
`CombatAtlasTestFixture.Atlas`/`.Sprite` resolve to `null` and every affected PlayMode test fails
at `Register(...)`'s "atlas is not configured" throw. This is on top of, and separate from, the
already-flagged production-atlas content-authoring gap above.

## Revision 3 (2026-07-03, same day): GetTexture() Does Not Exist

User reported a real compiler error via IDE selection: `error CS1061: 'SpriteAtlas' does not
contain a definition for 'GetTexture'`. Revision 2's `Register(...)` design had called
`Atlas.GetTexture()` to resolve the atlas's packed texture — that method was an incorrect
assumption on my part; it isn't part of `SpriteAtlas`'s public API. This is the second real API
mistake caught by the user in this same design area (the first being the `Texture2D`-vs-`SpriteAtlas`
field-type mistake in Revision 2), both caught because the user has live compiler/Editor feedback
this session did not have access to.

Fix, in `CombatRenderComponents.cs`'s `Register(...)`:
- Replaced `Atlas.GetTexture()` with `Sprite packedSprite = Atlas.GetSprite(sprite.name);` —
  `GetSprite(string name)` is `SpriteAtlas`'s actual documented runtime lookup API. It returns
  null both when the atlas hasn't been packed yet and when the named sprite isn't one of its
  packables — there is no separate public API to distinguish those two cases, so the two-throw
  design from Revision 2/task 002 (a distinct message for "not packed" vs. "not a member") was
  also incorrect and has been collapsed into one combined, honestly-worded throw.
- `Texture2D atlasTexture = packedSprite.texture;` resolves the packed texture from the *returned*
  sprite, not the original `sprite` parameter.
- Added a single-page guard not present before: if a later `Register(...)` call resolves a
  different `AtlasTexture` than an earlier call already cached, it throws — directly enforces the
  user's original "1 single page atlas" requirement, which nothing previously checked explicitly.
- UV rect and native-size-fold math now read `packedSprite.rect`/`.pixelsPerUnit`/`.texture`
  (the atlas's own copy) instead of the original `sprite` parameter's fields — more clearly correct
  post-pack, and avoids relying on an unverified assumption about reference redirection semantics.

Updated for consistency (all three had propagated the incorrect `GetTexture()` API): `index.md`
(added a third revision note, fixed remaining stale mentions in Constraints/Reused sections),
`002-atlas-configuration.md` (added a third revision note, rewrote the throw-cases list and code
samples), `003-registry-rework.md` (added a third revision note, fixed the Notes bullet),
`006-tests.md` (fixed one stale mention in the asset-spec section),
`Docs/contracts/render-batch-data.md` (fixed the Guarantees section),
`Assets/Tests/PlayMode/CombatAtlasTestFixture.cs` (fixed a comment). Grepped the whole repo for
`GetTexture` afterward to confirm no other stale mentions remained outside this historical note.

**Still not verifiable in this session:** no live Unity Editor/compiler available here, so this
fix is based on `SpriteAtlas.GetSprite`'s documented signature, not a confirmed successful
compile. Given two consecutive API mistakes in this same area, the recommended next step is for
the user to attempt a real compile again before trusting this is now correct.

## Notes

- **004 verification, done by hand-deriving actual test numbers, not just reasoning abstractly:**
  while implementing 006, found that `AoePlayModeTests.cs`'s two render-matrix assertions
  (`AoeVisualBakeUsesSpriteRendererTransformScale`, `AoeSpawnAreaSizeScalesCollisionAndRenderBeforeEcsSimulation`)
  needed real value changes, not just a compile fix. Both use a test sprite created via
  `Sprite.Create(Texture2D.whiteTexture, new Rect(0,0,1,1), pivot)` — a 1x1-pixel sprite at the
  `Sprite.Create` default `pixelsPerUnit = 100`, so its folded native size is `(0.01, 0.01)`.
  `geometry.VisualScale` in both tests evaluates to `(6, 8)` (verified against
  `AoeShape.FromTemplate`'s `baseVisualScale (from SpriteRenderer transform lossyScale) * resolvedAreaSize`
  formula and each test's specific `templateScale`/`visualScale`/`areaSize` inputs). Post-004 the
  matrix scale term is `geometry.VisualScale * entry.VisualScale = (6,8) * (0.01,0.01) = (0.06, 0.08)`.
  Updated both assertions from `(6f, 8f)` to `(0.06f, 0.08f)` with a comment explaining the
  derivation (not just adjusted until green).
- **006, other 4 test files:** confirmed via reading (not assumed) that `AoeSimulationTests.cs`,
  `BareMinimumPrototypePlayModeTests.cs`, `ProjectileCollisionSimulationTests.cs`,
  `ProjectileSpawnPipelineTests.cs` only do `CombatRenderBatchId`-value round-trip checks with
  arbitrary test ids, or archetype-construction (`typeof(CombatRenderBatchId)`/`typeof(CombatRenderElement)`)
  — none construct a real `CombatRenderResourceRegistry` or read render-matrix values, so none
  needed changes.
- **`Assets/Material/InstancedSprite*.mat`** (per `index.md` Open Question 2) are now vestigial
  (no longer referenced by any `Register(...)` call site) — left in place, not deleted, per the
  plan's explicit scope (asset deletion is a manual editor action, out of scope for an
  `.agent/`-only pass).
- **Sprite texture "Read/Write Enabled" requirement — checked and fixed.** The packer calls
  `sprite.texture.GetPixels(...)`, which throws if the source texture isn't marked readable.
  Traced all 14 combat prefabs' sprite GUIDs to their source textures and checked each `.meta`
  file directly (no Editor needed for this — `isReadable` is plain text in the `.meta` YAML): all
  7 distinct source textures had `isReadable: 0`. This would have thrown at the first
  `EnsureAtlasCurrent()` call. **Edited all 7 `.meta` files to `isReadable: 1`** so Unity reimports
  them with Read/Write enabled on next domain reload:
  - `Assets/Shooter/Interface/Tilemap/tilemap.png.meta`
  - `Assets/Shooter/Weapons/Tiles/tile_0029.png.meta`
  - `Assets/Shooter/Weapons/Tiles/tile_0024.png.meta`
  - `Assets/Shooter/Interface/Tiles/tile_0058.png.meta`
  - `Assets/Shooter/Interface/Tilemap/tilemap_packed.png.meta`
  - `Assets/Shooter/Other/approachcircle.png.meta`
  - `Assets/Shooter/Tiles/Tilemap/tilemap.png.meta`
  **Trade-off worth knowing:** these are shared tileset/tilemap sheets from the "Shooter" asset
  pack, used for more than just combat sprites (UI tiles, other tilemaps). Read/Write Enabled
  roughly doubles a texture's runtime memory (keeps a CPU-side copy alongside the GPU copy) for
  *every* use of that texture, not just the combat-sprite one — flagging this since it's a
  asset-import-setting change with a real (if probably small, given texture sizes) memory cost,
  not a pure code change.

## Validation Summary

Static only (source-level review, no live Unity compile or Test Runner run):
- Grepped `Assets/` for stray references to every removed member
  (`CombatSpriteRenderResources`, `_batchBuffers`, `ProjectileMeshName`, `AoeMeshName`,
  `entry.Resources`, `entry.Layer`, `entry.BoundsHalfExtent`) — none found.
- Grepped `Docs/` for stale "per-kind dictionary" wording — none found.
- Hand-traced the `AoePlayModeTests.cs` matrix-scale test fix through
  `AoeShape.FromTemplate`/`AoeTypeRegistry.TryBakeVisual`/`Sprite.Create` defaults rather than
  guessing expected values.
- Checked every combat prefab's sprite `.meta` file directly for `isReadable` (not skipped as
  "needs Editor" — this is plain-text YAML, checkable without Unity running) and fixed the 7 that
  were off.

Not done (no Unity Editor available in this environment):
- Live compile / domain reload.
- Manual Play Mode smoke test (fire projectiles/AOEs, confirm correct size/position/rotation,
  confirm draw call count drops in Profiler/Frame Debugger, confirm a mid-run-registered kind
  renders correctly after an atlas repack).
- PlayMode Test Runner execution.

**Recommended next step:** open the project in the Unity Editor, let it compile, then run the
verification steps from `index.md`'s (well, this plan was written directly to
`.agent/combat-render-atlas/index.md`, no separate plan-mode summary carries a "Verification"
section — see the equivalent checklist above) — particularly the Read/Write Enabled check on
sprite textures, since that's the one dependency this session couldn't verify at all.

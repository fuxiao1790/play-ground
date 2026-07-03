# Tests

*(2026-07-03 addendum: the atlas-configuration revision adds a new requirement below — every test
fixture that creates a real `CombatRoot` and registers a real sprite must configure the atlas, or
`Register(...)` throws per task 002's validation. Already applied during implementation; recorded
here for the record.)*

*(2026-07-03, revised again the same day: the atlas field changed from `Texture2D` to
`UnityEngine.U2D.SpriteAtlas` — see task 002's second revision note. This invalidated the original
"just pass `Texture2D.whiteTexture` everywhere" fixture fix below, since a real `SpriteAtlas` can
only validate sprites that were actually packed into it via Unity's real asset pipeline, which an
in-memory `Sprite.Create(...)` sprite never is. The fix described in this section is the *current*
one; the strikethrough-equivalent original ("use `Texture2D.whiteTexture` uniformly") no longer
applies and has been superseded, not left in place.)*

## Current Requirement: Real Atlas + Real Sprite Asset In Test Fixtures

Every fixture helper that creates a `CombatRoot` via `AddComponent<CombatRoot>()` and goes on to
register a real sprite (directly or via `RegisterTemplate`/`RegisterType`/`RegisterConfig`) must
call `root.ConfigureAtlas(atlas)` before `SetActive(true)` (the same pre-`Awake` window the
existing `root.Configure(sprite)` pattern already uses — `Awake()` is what actually invokes
`ConfigureAtlas`/`Register` on the real registry), **and** the sprite it registers must be a real,
already-packed member of that atlas — not `Sprite.Create(Texture2D.whiteTexture, ...)`.

User-directed resolution: a real test atlas asset at
`Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2`, backed by a real source texture at
`Assets/Tests/TestAssets/CombatAtlasTestSource.png` (see "Required Test Asset Specification"
below), loaded via a new `#if UNITY_EDITOR`-guarded helper,
`Assets/Tests/PlayMode/CombatAtlasTestFixture.cs` (`CombatAtlasTestFixture.Atlas` /
`CombatAtlasTestFixture.Sprite`, both `AssetDatabase.LoadAssetAtPath`/`LoadAllAssetsAtPath`-backed,
cached in static fields). Every `Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f),
Vector2.one * 0.5f)` call site was replaced with `CombatAtlasTestFixture.Sprite`, and every
`root.ConfigureAtlas(Texture2D.whiteTexture)` call site was replaced with
`root.ConfigureAtlas(CombatAtlasTestFixture.Atlas)`.

`CombatAtlasTestFixture.Sprite` intentionally loads the *original* asset-database sprite reference
(`AssetDatabase.LoadAllAssetsAtPath(...).OfType<Sprite>()`), not `SpriteAtlas.GetSprite(name)` —
this matches how production code actually receives sprites (a skill prefab's authored `Sprite`
field, whose `.texture` Unity's Sprite Packing system transparently redirects to the atlas's
packed page once packed), so the test exercises the same code path real gameplay does.

Found and fixed the same 6 `CombatRoot`-creating fixture helpers as the first revision
(`AoePlayModeTests.cs`: `CreateAoeFixture`, `CreateProjectileRoot`;
`BareMinimumPrototypePlayModeTests.cs`: `CombatRootRegistersTemplateBeforeAwake`,
`GameRootAcceptsAuthoredSpawnerWhenNoSceneMobsAreAuthored`, `CreateProjectileHitFixture`,
`CreateAoeFixture`). `AoeSimulationTests.cs`, `ProjectileCollisionSimulationTests.cs`,
`ProjectileSpawnPipelineTests.cs` never construct a real `CombatRoot` (they hand-build bare
`World`s and entities directly), so they're unaffected by this requirement.

## Required Test Asset Specification (Not Yet Created — User Action)

The code assumes but does not itself create these two asset files (binary/Editor-asset creation is
outside what this session can author or verify without a live Unity Editor):

- `Assets/Tests/TestAssets/CombatAtlasTestSource.png` — any small image, imported as Texture Type
  "Sprite (2D and UI)". **Must** produce exactly one `Sprite` sub-asset with pixel rect `(0, 0, 1,
  1)` (i.e. a single 1×1px sprite — Sprite Mode "Single" with the whole image treated as one
  sprite works if the source image itself is 1×1px; a larger image needs Sprite Mode "Single" too,
  since "Multiple" mode with a 1×1 slice is unnecessary complexity here), pivot `(0.5, 0.5)`,
  Pixels Per Unit `100` (Unity's default — matches what `Sprite.Create(..., new Rect(0,0,1,1),
  Vector2.one * 0.5f)` produced before, so every existing hand-derived assertion, e.g. task 004's
  `(0.06, 0.08)` AOE matrix-scale values, stays correct with no re-derivation).
- `Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2` — a `SpriteAtlas` asset with that
  texture's sprite added as a packable, packed (Pack Preview, or automatic for Sprite Atlas V2)
  so `Atlas.GetSprite("CombatAtlasTestSource")` (or whatever the sprite's actual name resolves to)
  returns non-null and its `.texture` is the redirected, packed one.

Until these exist, `CombatAtlasTestFixture.Atlas`/`.Sprite` resolve to `null` and every affected
PlayMode test fails at the `Register(...)` call with the "atlas is not configured" exception.

## Change

Update/verify the 5 PlayMode test files that reference render types, to
match the atlas rework. Most are expected to need no changes (they exercise
`CombatRenderBatchId` round-tripping through reuse with arbitrary test ids,
unaffected by the dictionary-key → UV-table-index reinterpretation) — verify
each rather than assuming.

### Files to check

- `Assets/Tests/PlayMode/AoePlayModeTests.cs` — **expected to need a real
  change.** Locate the helper that reads `CombatRenderElement.objectToWorld`
  (or similar render-matrix assertions) and its callers. Since AOE visual
  size now depends on the task-004 fold (`geometry.VisualScale * entry.VisualScale`)
  rather than `geometry.VisualScale` alone plus mesh-baked pixel size, any
  assertion on matrix scale terms needs its expected values recalculated
  against the new formula. Recompute expected values by hand from the same
  sprite/scale inputs the test already sets up, rather than just adjusting
  numbers until the test passes.
- `Assets/Tests/PlayMode/AoeSimulationTests.cs` — check for
  `CombatRenderBatchId`/render-matrix assertions; expected to be unaffected
  (arbitrary test ids), confirm by reading.
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs` — check for a
  render-instance-counting helper keyed on `CombatRenderBatchId.Value`;
  expected to remain valid since real render ids are still stable identifiers
  (now indexing a UV table instead of a dictionary) — confirm by reading, no
  change expected.
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs` — check for
  render-type assertions; expected unaffected.
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs` — check for
  render-type assertions; expected unaffected.

### General checks across all 5 files

- Any hand-built entity archetype in test setup code that lists
  `typeof(CombatRenderComponent)`, `typeof(CombatRenderBatchId)`,
  `typeof(CombatRenderElement)`, etc. needs **no** change — these component
  *shapes* are untouched by this plan (only `CombatRenderResourceRegistry`'s
  internals and `CombatBatchedRenderSystem`'s submit loop change).
- If any test directly constructs a `CombatRenderResourceRegistry` or calls
  `Register(...)`, update the call to the new signature (task 003 drops
  `sourceMaterial`/`meshName` parameters).

## Acceptance Criteria

- All 5 files compile against the post-003/004/005 API.
- `AoePlayModeTests` render-matrix assertions pass with recalculated expected
  values (not just adjusted until green — the reasoning for the new expected
  value should be traceable to the task-004 formula).
- The other 4 files pass unchanged, confirming the "unaffected" expectation
  was correct (if any of them turn out to need a change, that's new
  information — document what was actually wrong, don't silently patch).
- Full PlayMode suite run (Unity Test Runner) is green.
- (2026-07-03 SpriteAtlas revision) `CombatAtlasTestFixture.cs` compiles and,
  once the two required test assets exist (see "Required Test Asset
  Specification" above), every affected fixture's `Register(...)` call
  succeeds instead of throwing — this cannot be verified in this session (no
  Unity Editor available), so it remains an open item until run once in the
  Editor.

## Dependencies

003 (registry rework), 004 (visual scale folding), 005 (batched render
system rework).

## Scope

Medium.

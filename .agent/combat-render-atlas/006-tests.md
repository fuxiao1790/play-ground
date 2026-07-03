# Tests

*(2026-07-03 addendum: the atlas-configuration revision adds a new requirement below — every test
fixture that creates a real `CombatRoot` and registers a real sprite must configure the atlas
texture, or `Register(...)` throws per task 002's validation. Already applied during
implementation; recorded here for the record.)*

## New Requirement: Atlas Configuration In Test Fixtures

Every fixture helper that creates a `CombatRoot` via `AddComponent<CombatRoot>()` and goes on to
register a real sprite (directly or via `RegisterTemplate`/`RegisterType`/`RegisterConfig`) must
call `root.ConfigureAtlas(texture)` before `SetActive(true)` (the same pre-`Awake` window the
existing `root.Configure(sprite)` pattern already uses — `Awake()` is what actually invokes
`ConfigureAtlas`/`Register` on the real registry). All synthetic test sprites already use
`Sprite.Create(Texture2D.whiteTexture, ...)` consistently across `AoePlayModeTests.cs` and
`BareMinimumPrototypePlayModeTests.cs`, so `root.ConfigureAtlas(Texture2D.whiteTexture)` is the
natural, uniform choice — the same texture object as every test sprite's `.texture`, satisfying
`Register(...)`'s `sprite.texture == AtlasTexture` check.

Found and fixed 6 `CombatRoot`-creating fixture helpers across the two files that needed this
(`AoePlayModeTests.cs`: `CreateAoeFixture`, `CreateProjectileRoot`;
`BareMinimumPrototypePlayModeTests.cs`: `CombatRootRegistersTemplateBeforeAwake`,
`GameRootAcceptsAuthoredSpawnerWhenNoSceneMobsAreAuthored`, `CreateProjectileHitFixture`,
`CreateAoeFixture`). `AoeSimulationTests.cs`, `ProjectileCollisionSimulationTests.cs`,
`ProjectileSpawnPipelineTests.cs` never construct a real `CombatRoot` (they hand-build bare
`World`s and entities directly), so they're unaffected by this requirement.

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

## Dependencies

003 (registry rework), 004 (visual scale folding), 005 (batched render
system rework).

## Scope

Medium.

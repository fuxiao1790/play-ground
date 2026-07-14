# 005 — Tests and doc

## Goal
Update every site that constructs the old `CombatHitEvent` or the old component
shape, plus the contract doc.

## Test updates

### Direct `CombatHitEvent` enqueues → now need a real source entity
`AoeSimulationTests` enqueues events with inline damage
([:1766-1790](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1766-L1790)). These
must instead create a source entity with a `CombatHitPayload` component and enqueue
`{ Source = src, Target = tgt }`. This is the biggest test-shape change: pushing a
raw damage number into finalize now requires materializing a source. Add a small
helper (e.g. `CreateHitSource(CombatHitPayload)`) to keep call sites terse.

### Entity-constructing tests → set the `CombatHitPayload` component
Tests that `SetComponentData(entity, new ProjectileHitComponent { HitPayload = … })`
must also set the standalone `CombatHitPayload` component and use the slimmed
`ProjectileHitComponent`:
- `ProjectileCollisionSimulationTests` ([:432-440](../../Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs#L432-L440))
- `ProjectileTrackingSimulationTests` ([:347-355](../../Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs#L347-L355))

### `hit.SourceNodeId` reader
`BareMinimumPrototypePlayModeTests` reads `hit.SourceNodeId`
([:1015](../../Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs#L1015)).
Re-source it from `PayloadLookup[hit.Source].SourceNodeId`, or drop the assertion if
it only guarded the now-removed field.

### Authoring-config test sites — no change
`new ProjectileHitPayload(new CombatHitPayload{…})` inside spawn configs/commands
(AoePlayModeTests, ProjectileAuthoringEditModeTests, ProjectileSpawnPipelineTests,
CombatPoolCleanupSystemTests, SpawnCommandUnificationTests, etc.) stay as-is — the
authoring DTO is unchanged; only the entity write moved.

## Doc update
[combat-hit-and-tick-results.md](../../Docs/contracts/combat-hit-and-tick-results.md):
- `CombatHitEvent` shape is now `{ Source, Target }`; damage/crit/stack/source-node
  data lives on the source entity's `CombatHitPayload` component and is read by
  finalize via `ComponentLookup`.
- Update the "Fields / Shape" list; note the source-lookup guarantee under
  "Guarantees".

## Acceptance
- All EditMode + PlayMode combat tests compile and pass (user-run).
- Doc reflects the `{Source, Target}` event and source-side payload.

## Dependencies
Requires 001–004. Scope: medium (many test sites, mostly mechanical).

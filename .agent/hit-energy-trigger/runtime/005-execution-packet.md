# Task Execution Packet

## Task
005-assets-and-docs.md

## Goal
Migrate all current authored content to explicit HitEnergy fields with stable GUIDs, then rewrite current documentation to describe only the new model.

## Files Allowed To Modify
- All Skill `.asset` files under `Assets/ScriptableObjects/Skills/Skill/`
- All Skill `.asset` files under `Assets/ScriptableObjects/Mobs/Skills/Skill/`
- Current trigger assets under `Assets/ScriptableObjects/Skills/Triggers/` and misfoldered trigger assets under `Assets/ScriptableObjects/Skills/Supports/`, plus matching `.meta` files during rename
- Catalog/loadout/skill-set asset references only if filename/type migration directly requires serialized correction while preserving GUID membership
- Minimum docs listed in task 005
- Additional current `Docs/` files containing old feature vocabulary found by repository search
- `.agent/hit-energy-trigger/implementation-log.md`

## Files Allowed To Create
- Renamed HitEnergy trigger asset filenames paired with their original `.meta` GUID contents.

## Files Allowed To Delete
- Obsolete trigger asset filenames after GUID-preserving rename.

## Files Likely Needed For Reading
- Skill/trigger YAML assets and their metas.
- Trigger catalog and skill-set references by GUID.
- Production type names/data flow from tasks 001-003.
- Current docs returned by legacy-vocabulary search.

## Behavior To Preserve
- Every asset GUID and all catalog/loadout references.
- Existing hit timing baseline: contribution multiplier = old stacks per hit; requirement multiplier = old threshold; retention = old lifetime.
- Interval energy semantics and vocabulary.

## Behavior To Change
- Every Skill asset explicitly serializes `triggerEnergy: 1`.
- Every legacy trigger asset becomes a `HitEnergyTrigger` asset with explicit positive multipliers and retention.
- Obsolete asset filenames, object/display names, class identifiers, and serialized keys become HitEnergy vocabulary.
- Current docs describe only HitEnergy model and correct phase ordering.

## Relevant Global Context
- `TriggerEnergy` has dual role: source base contribution and triggered-skill base requirement.
- `RuntimeHitEnergyTrigger` is edge composition.
- Edge identity is unique `AccumulatorId`, independent of reused assets/equal values/template keys.
- Flow: `HitEnergyPayload` -> `TargetHitEnergy` -> `HitEnergyActivationSystem` -> existing spawn lanes.
- Float remainder/overflow persists; current-update deposit activates next update.
- `HitEnergySpawn` template reference is sole output-stat source.

## Dependencies Confirmed
- Tasks 001-004 complete by static inspection.
- `HitEnergyTrigger.cs` retains old StackTrigger MonoScript GUID `c9180de4c19e45f9b312fa61190d9306`.
- Current trigger asset GUIDs and catalog/skill-set references were inventoried before migration.

## Step-By-Step Instructions
1. Add explicit `triggerEnergy: 1` to every Skill asset in both specified directories.
2. Transform every legacy trigger asset, including misfoldered Supports assets, using exact old-to-new field mapping.
3. Point assets at `HitEnergyTrigger` MonoScript where required; update class identifiers.
4. Rename obsolete trigger asset filenames and matching metas together; preserve meta GUIDs, internal asset GUIDs, and references.
5. Remove every old serialized key and old feature display/object name.
6. Update all minimum docs plus any other current docs found by legacy search. Remove count formulas and old feature vocabulary; document formulas, composition, identity, adjacency, flow, float remainder, template authority, interval separation, and phase order.
7. Do not place migration-history wording in current design docs.

## Acceptance Criteria
- Every specified Skill asset has explicit positive `triggerEnergy`.
- Every HitEnergy trigger asset has explicit positive contribution/requirement multipliers and `retentionSeconds`.
- Script and asset GUIDs remain unchanged; catalog membership stays intact.
- Production/docs/assets search finds no old feature symbols or mixed vocabulary.
- Docs state adjacency and same-asset edge isolation globally and distinguish interval energy.

## Validation Required
- User deferred validation until this task completes.
- Perform GUID before/after checks, serialized-field counts, repository legacy-symbol searches, and `git diff --check`.
- Do not run Unity tests or Unity test runner.
- Update log with exact final user-run XML requirements.

## Hard Boundaries
- Do not change balance values beyond exact migration baseline.
- Do not add `FormerlySerializedAs`, compatibility properties, aliases, or adapters.
- Do not modify unrelated assets/docs.
- Preserve all GUIDs and references.
- Stop on ambiguous asset identity/type mapping.

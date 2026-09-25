# 005 - Migrate Assets And Rewrite Documentation Vocabulary

## Goal

Transform current content once, preserve asset identity, and document only new
HitEnergy model.

## Changes

### Assets and type rename

1. Add explicit `triggerEnergy: 1` to all Skill assets under:
   - `Assets/ScriptableObjects/Skills/Skill/`
   - `Assets/ScriptableObjects/Mobs/Skills/Skill/`
2. Rename StackTrigger script/class to HitEnergyTrigger while preserving its
   `.meta` GUID so catalogs/loadouts retain references.
3. Transform every current trigger asset, including misfoldered ones under
   `Assets/ScriptableObjects/Skills/Supports/`:

   ```text
   energyContributionMultiplier = old stacksPerHit
   energyRequirementMultiplier  = old stackThreshold
   retentionSeconds              = old debuffLifetimeSeconds
   ```

4. Remove old serialized keys after transformation. Do not retain runtime
   compatibility properties or FormerlySerializedAs aliases.
5. If asset filenames/display names use obsolete terminology, rename files with
   their `.meta` files so GUIDs remain stable. Catalog membership stays intact.

### Documentation

Update at minimum:

- `Docs/folder-structure.md`
- `Docs/reference/game-logic/skill-gameplay-system.md`
- `Docs/reference/game-logic/skill-system.md`
- `Docs/reference/simulation/skill-ecs-simulation.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/contracts/skill-runtime-snapshots.md`
- `Docs/contracts/combat-hit-and-tick-results.md`
- `Docs/contracts/target-proxy.md`
- `Docs/flows/collision-to-combat-result.md`
- `Docs/architecture/phase-order.md`
- `Docs/testing.md`

Document only fresh model:

1. TriggerEnergy and two multiplier formulas.
2. RuntimeHitEnergyTrigger as edge composition, not skill subclass.
3. AccumulatorId as per-compiled-edge identity.
4. Same-asset nodes isolated by position and edge id.
5. HitEnergyPayload -> TargetHitEnergy -> HitEnergyActivationSystem flow.
6. Float activation/remainder and next-update timing.
7. HitEnergySpawn template reference as output source of truth.
8. Interval energy remains separate.
9. Correct phase order: activation processing occurs before current-update hit
   finalization, so new deposits process next update.

Remove old count formulas and stack/debuff/detonation terminology for this
feature. Historical migration notes belong in plan/commit, not current design
docs.

## Acceptance Criteria

- Every Skill asset has explicit positive triggerEnergy.
- Every HitEnergyTrigger asset has two explicit positive multipliers and
  RetentionSeconds.
- Script/asset GUID references remain stable.
- Production/docs search finds no old feature symbols or mixed vocabulary.
- Docs state adjacency/same-asset isolation as global rule.
- Docs clearly distinguish TriggerEnergy from interval energy.

## Dependencies

- Tasks 001-004.

## Scope / Complexity

Medium-high. GUID-safe script/asset rename, YAML transformation, and broad docs
rewrite.

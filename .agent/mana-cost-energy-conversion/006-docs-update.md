# 006 — Docs update

## Goal
Keep the design docs consistent with the new mana-cost model.

## Changes

1. **`Docs/reference/game-logic/skill-system.md`**
   - `ProjectileDefinition` / `AoeDefinition` / `LingeringAoeDefinition` behavior
     lists: rename `spawnEnergyCost` → `manaCost` and update the surrounding
     paragraph. The old text ("energy threshold only when spawned by a timed child
     trigger") becomes: mana cost folds through the stat system (supports modify
     it) and the interval-spawn **trigger link** converts the child's folded mana
     cost into the energy threshold via its `ManaToEnergyCost` function
     (`manaToEnergyRatio`).
   - Interval-trigger sections: note that `energyPerSecond` / `energyJitterPercent`
     now live on a shared `IntervalSpawnTrigger` base, and that the child energy
     threshold is `childManaCost * manaToEnergyRatio` (no longer a raw authored
     `spawnEnergyCost`).
   - Supports table: add a "mana cost" column or note that
     Multiple Projectiles / Piercing / Homing / Multiple AOEs contribute
     `SkillStat.ManaCost`.

2. **`Docs/reference/simulation/spawn-template-registry.md`** — if it mentions
   `spawnEnergyCost` as the threshold source, update to "child mana cost converted
   by the trigger link."

3. **New short section** (skill-system.md or a stats doc) describing player mana:
   authored on `UnitStatSheet.maxMana`, seeded into the ECS `TargetMana` component
   at proxy creation exactly like `TargetHealth`, owned by ECS thereafter.
   Note that consumption/gating is not yet wired (Decision E).

4. **`Docs/todo.md`** — mark the "skills should have a mana cost…" bullet as in
   progress / done as appropriate; note the deferred consumption follow-up.

## Acceptance Criteria
- No remaining references to `spawnEnergyCost` in `Docs/` except clearly-labeled
  historical notes.
- Docs describe: mana cost folds with supports; conversion lives on the trigger
  link; player mana is ECS-owned like health; consumption deferred.

## Dependencies
Depends on 001–004 (documents the final shapes).

## Scope
Small.

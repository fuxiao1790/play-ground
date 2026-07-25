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

3. **New "Unit Resources" doc section** (a stats/resources doc + update
   `Docs/contracts/target-proxy.md`): health and mana are one agnostic resource
   concept. Document the ownership split — GameObject owns `Max`/regen-rate/initial
   (from `UnitStatSheet`) and pushes `Max` on change; ECS owns `Current` and the
   regen tick (`ResourceRegenSystem`); the root mirrors `Current` for presentation.
   Note the neutral component names (`Health`/`Mana`, no `Target` prefix) and that
   the same shared managed `Resource` type serves any unit. Note consumption/gating
   is still deferred (index open question 1).

4. **Spend transaction doc** (new short flow, e.g. `Docs/flows/` + a note in
   `skill-system.md`): using a skill emits a `ResourceSpendRequest`; the serial
   `ResourceSpendSystem` applies it to the caster's `Mana` and emits
   accept/reject; `ResourceSpendBridge` returns the result to the `SkillDriver`,
   which spawns only on accept (root casts only; interval children stay
   energy-funded). Note the ~1-frame cast latency this introduces.

5. **`Docs/todo.md`** — mark the "skills should have a mana cost…" bullet as in
   progress / done as appropriate; note that spend + regen are now implemented and
   the resource model is unified.

## Acceptance Criteria
- No remaining references to `spawnEnergyCost` in `Docs/` except clearly-labeled
  historical notes.
- Docs describe: mana cost folds with supports; conversion lives on the trigger
  link; player mana is ECS-owned like health; consumption deferred.

## Dependencies
Depends on 001–004 (documents the final shapes).

## Scope
Small.

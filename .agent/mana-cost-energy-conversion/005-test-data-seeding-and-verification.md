# 005 — Test-data seeding (editor steps for user) + verification

## Goal
Populate non-zero mana / mana-cost values so the feature can be exercised, and
define how to confirm it works. Per memory `editor-steps-are-user-steps`, all SO
asset and prefab value edits are **instructions for the user in the Unity
editor**, not hand-edited YAML.

## Editor steps (user performs in Unity)
1. **Player stat sheet** — on the player's `UnitStatSheet` asset set `Max Mana`
   to a non-zero value (e.g. `100`) and `Mana Regen Per Second` to a non-zero
   value (e.g. `5`). Leave `Health Regen Per Second` at `0` unless you want health
   to regen too.
2. **Skill assets** — on a couple of `ProjectileSkill` / `AoeSkill` assets set
   `Mana Cost` to distinct non-zero values (e.g. `4` and `8`). Existing assets
   already migrate their old `spawnEnergyCost` value via `[FormerlySerializedAs]`,
   so only override where you want a specific test number.
3. **Support assets** — on `MultipleProjectilesSupport`, `PiercingSupport`,
   `HomingSupport` (and optionally the AOE/damage supports) set `Mana Cost Added`
   to non-zero values (e.g. `4`, `3`, `3`).
4. **Trigger assets** — leave `Mana To Energy Ratio` at `1` for the first check;
   optionally make a second `ProjectileIntervalSpawnTrigger` asset with ratio `2`
   to see the threshold double.
5. Wire a loadout: a root skill → `ProjectileIntervalSpawnTrigger` → a child
   projectile set, then add/remove supports on the **child** set.

## Verification
- **Automated (EditMode):** the new compiler tests from 003 assert
  `EnergyThreshold` = `childManaCost * manaToEnergyRatio`, and that a child support
  raises the threshold.
- **Automated (PlayMode):** a test that creates the player proxy and asserts
  `Mana.Max == statSheet.MaxMana` and `Mana.RegenPerSecond == statSheet` regen
  (from 004), and that `Mana.Current` rises over ticks toward `Max` (regen, 007).
- **Automated (resource unification):** existing player/mob health PlayMode tests
  still pass through the shared `Resource` type; a Max-change push updates `Max`
  without resetting ECS-owned `Current` (004).
- **Automated (spend, 009/010):** a spend request with enough mana deducts
  `ManaCost` and returns accepted; with too little mana it returns rejected and
  changes nothing; two casters spending the same frame don't race; a zero-cost
  skill bypasses the transaction.
- **Manual (spend):** set a root skill's mana cost high relative to the pool and
  fire repeatedly — casts should stop when mana runs low and resume as it
  regenerates; cooldown is not consumed on a rejected cast.
- **Manual in-play:** with the loadout above, adding Multiple Projectiles/Piercing/
  Homing to the child set visibly **slows** the interval child spawn cadence
  (higher mana cost → higher energy threshold → longer to accrue), and raising the
  trigger's `manaToEnergyRatio` slows it further. Removing supports speeds it back
  up.

## Acceptance Criteria
- All automated tests green.
- Manual check: support changes on the child set change child spawn frequency in
  the expected direction, with no re-tuning of any energy field.

## Dependencies
Depends on 001–004.

## Scope
Small (mostly editor steps + a couple of tests).

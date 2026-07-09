# 002 — Equip an EXISTING loadout (no new authoring)

## Goal
Do **not** author any new skill/set/loadout assets. Mobs equip an **existing**
`SkillLoadout` from `Assets/ScriptableObjects/Skills/Loadout/`. This directly exercises the
owner-agnostic invariant (#9): the same asset a player can equip is dropped onto a mob.

## Choose an existing loadout
Any asset under `Assets/ScriptableObjects/Skills/Loadout/` is valid. Options:
- **Reuse the player's current loadout** — `ProjectileStackTriggerLoadout.asset` (guid
  `a2000000000000000000000000000005`, referenced by `Player.prefab`'s `SkillDriver`). This
  is the most literal proof that one asset serves both owners.
- **Or pick a simpler projectile loadout** for calmer mob behavior (e.g.
  `ArrowOnImpactAoeLoadout.asset` or any plain single-projectile loadout in that folder).

Selection is a tuning choice for the wiring step (003); no asset creation or edits here.

## Acceptance criteria
- No new `.asset` files are created for this feature.
- The chosen existing loadout compiles clean on a mob `SkillDriver`
  (`SkillLoadoutValidator.Validate` warning-free) and fires.
- The *same* asset remains equippable on the player with no per-owner variant.

## Scope
Trivial — a selection decision, no code, no authoring.

## Dependencies
None. Feeds 003 (the asset assigned to the mob prefab's `SkillDriver`).

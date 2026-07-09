# 003 — Add + wire SkillDriver on mob prefabs

## Goal
Attach a `SkillDriver` to each mob prefab and wire it in the Inspector so mobs fire as
`CombatFaction.Mob` using the mob loadout. Inspector wiring (not Awake injection) per user
preference for edit-time-known values.

## Prefabs
`Assets/Prefabs/Mobs/Bat.prefab`, `Skeleton.prefab`, `Slime.prefab`.
(All are trivial `MobRoot` subclasses — `BatRoot`/`SkeletonRoot`/`SlimeRoot` — so the 001
behavior is inherited by each.)

## Per prefab
Add a `SkillDriver` component on the mob root GameObject and set:
- `loadout` = the **existing** `SkillLoadout` chosen in 002 (no new authoring). Any loadout
  under `Assets/ScriptableObjects/Skills/Loadout/` is valid; a mob may equip any loadout,
  including the one the player uses (`ProjectileStackTriggerLoadout.asset`) — invariant 9.
- `faction` = `CombatFaction.Mob`.
- `statSheet` = the shared `MobStatSheet.asset` (so mob offense stats feed
  `SkillStatAggregator`).
- `combatRoot` = leave empty. `SkillDriver.Awake` resolves the scene `CombatRoot` via the
  `PlayerProjectileRoot` tag, and `MobRoot.BindCombatRoot` (001) re-binds it. Both resolve
  to the same unified root.
- `vfxRoot` / `audioManager` = leave empty/auto unless mob VFX/SFX is wanted now.

## Optional: runtime-built prefab path
`MobSpawnerRoot.CreateRuntimeMobPrefab` (tests/benchmarks) builds mobs in code. Adding a
`SkillDriver` there is optional; without a loadout it is a harmless no-op. Skip unless
mob attacks are needed in that benchmark path.

## Acceptance criteria
- Play a scene with player + `MobSpawnerRoot`; spawned Bat/Skeleton/Slime fire projectiles
  toward the player.
- Projectiles damage the player and pass through other mobs (faction gate).
- Player combat unaffected (still hits mobs).

## Scope
Small — prefab edits, no code.

## Dependencies
001 (`MobRoot` drives `SkillDriver`, `BindCombatRoot` forwards) and 002 (loadout asset).

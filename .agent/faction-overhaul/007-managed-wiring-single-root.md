# 007 — Wire scene/managed callers to one root + own faction

## Goal
Collapse every managed reference from "player root + mob root" to a single `CombatRoot`,
and have each caller pass its own faction to the spawn API (006).

## Changes
1. **`GameRoot`** ([GameRoot.cs](../../Assets/Scripts/Game/GameRoot.cs)):
   - One `[SerializeField] CombatRoot combatRoot;` (remove `playerCombatRoot` /
     `mobCombatRoot`). Update both `Configure(...)` overloads.
   - Register **all** mobs and the player to `combatRoot.TargetRegistry` (drop the
     `CanTarget` gate, removed in 001/006).
   - `mob.BindCombatRoot(combatRoot)`; the mob's status-AOE and own projectile attack
     resolve faction internally (see MobRoot below).
   - `driver.BindCombatRoot(combatRoot)`.
2. **`MobRoot`** ([MobRoot.cs](../../Assets/Scripts/Mob/MobRoot.cs)):
   - Implement `CombatFaction => CombatFaction.Mob` (001).
   - Collapse `aoeCombatRoot` into `combatRoot`; `BindAoeRoot` either forwards to the
     same root or is removed. Status-triggered AOE
     ([MobRoot.cs:355-369](../../Assets/Scripts/Mob/MobRoot.cs#L355-L369)) calls
     `combatRoot.Spawn(request, CombatFaction.Player)` (it damages mobs → Player faction).
   - `MobProjectileAttack` is constructed with the one root; its spawns are `Mob`.
3. **`MobProjectileAttack`** ([MobProjectileAttack.cs](../../Assets/Scripts/Mob/MobProjectileAttack.cs)):
   - `projectileRoot.Spawn(request, CombatFaction.Mob)`.
4. **`MobSpawnerRoot`** ([MobSpawnerRoot.cs](../../Assets/Scripts/Spawn/MobSpawnerRoot.cs)):
   - One `combatRoot` field; `BindCombatRoots(...)` → `BindCombatRoot(CombatRoot)`.
   - `RegisterMob`/`BindCombatRoots` register mobs to `combatRoot.TargetRegistry`,
     `mob.BindCombatRoot(combatRoot)` (no separate AOE root).
   - `GameRoot.BindCombatRoots` call site (L51) updates accordingly.
5. **`PlayerSkillDriver`** ([PlayerSkillDriver.cs](../../Assets/Scripts/Skills/PlayerSkillDriver.cs)):
   - `vfxRoot.BindFaction(CombatFaction.Player)` (replace `combatRoot.Faction`, L42/L75).
   - Spawns go through `SkillSpawnTranslator` with `CombatFaction.Player`.
   - `SkillIntervalTemplateBuilder.Build*StackEffectSnapshot`: set
     `Faction = CombatFaction.None` (L786, L813) — apply re-stamps (index I2/I3).
6. **`SkillSpawnTranslator`** ([SkillSpawnTranslator.cs](../../Assets/Scripts/Skills/SkillSpawnTranslator.cs)):
   - Add a `CombatFaction faction` parameter to `Spawn(...)` and forward it to
     `SpawnRegisteredProjectile` / `SpawnRegisteredAoe`. `PlayerSkillDriver.Tick` passes
     `CombatFaction.Player`.
7. **VFX: keep compiling only (out of scope).** The only required VFX touch is replacing
   the broken `combatRoot.Faction` read in `PlayerSkillDriver` with literal
   `CombatFaction.Player` (item 5). Do **not** rework VFX faction routing or fix mob VFX
   binding in this overhaul — `VfxSpawnRequestElement.Faction` still flows from
   `identity.Faction`, so VFX dispatch keeps working as-is for whatever roots are bound.
7. **Scene (manual):** in the gameplay/benchmark scenes, delete the second `CombatRoot`
   GameObject and point `GameRoot`/`MobSpawnerRoot`/`PlayerSkillDriver` serialized refs
   at the single `CombatRoot`. (`CombatVfxRoot`s stay two, faction-keyed.)

## Acceptance Criteria
- No managed code holds two `CombatRoot` references.
- Player projectiles/AOEs spawn `Player`; mob projectiles spawn `Mob`; mob status AOE
  spawns `Player`.
- Mobs and the player are registered to the same `TargetRegistry` with their own faction.
- Game runs: player damages mobs, mobs damage player, no friendly fire.

## Dependencies
006 (faction-per-spawn API). Last code task before tests/docs.

## Scope
Medium, many small call-site edits + a manual scene edit.

# Task Execution Packet

## Task
003-wire-mob-prefabs.md

## Goal
Attach and wire `SkillDriver` on Bat, Skeleton, and Slime prefabs so spawned mobs fire as `CombatFaction.Mob`.

## Files Allowed To Modify
- `Assets/Prefabs/Mobs/Bat.prefab`
- `Assets/Prefabs/Mobs/Skeleton.prefab`
- `Assets/Prefabs/Mobs/Slime.prefab`
- `.agent/mob-skills/implementation-log.md`

## Files Allowed To Create
- `.agent/mob-skills/runtime/003-execution-packet.md`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Prefabs/Player/Player.prefab`
- `Assets/Scripts/Skills/SkillDriver.cs.meta`
- `Assets/ScriptableObjects/Skills/Loadout/ProjectileStackTriggerLoadout.asset.meta`
- `Assets/ScriptableObjects/Mobs/MobStatSheet.asset.meta`
- `Assets/Scripts/System/Core/CombatScope.cs`

## Behavior To Preserve
- Existing mob root component wiring.
- Existing player combat.
- `combatRoot`, `vfxRoot`, and `audioManager` stay unassigned on mob prefab `SkillDriver`.

## Behavior To Change
- Each mob root GameObject gains one `SkillDriver`.
- Each `SkillDriver` uses the selected existing loadout, `CombatFaction.Mob`, and `MobStatSheet.asset`.

## Relevant Global Context
- `MobRoot` now drives an optional `SkillDriver`.
- `SkillDriver.Awake` can resolve the shared combat root, and `MobRoot.BindCombatRoot` can re-bind it.
- Loadout assets remain owner-agnostic.

## Dependencies Confirmed
- Task 001 complete: `MobRoot` resolves and ticks optional `SkillDriver`.
- Task 002 complete: selected `ProjectileStackTriggerLoadout.asset` GUID `a2000000000000000000000000000005`.
- `SkillDriver.cs` GUID is `a100000000000000000000000000000d`.
- `MobStatSheet.asset` GUID is `4f1fe3388cd7fef42b7f7bd6e5bc3cc2`.
- `CombatFaction.Mob` serializes as `2`.

## Step-By-Step Instructions
- Add one `SkillDriver` MonoBehaviour to each mob root GameObject component list.
- Set `loadout` to `ProjectileStackTriggerLoadout.asset`.
- Leave `combatRoot`, `vfxRoot`, and `audioManager` empty.
- Set `statSheet` to `MobStatSheet.asset`.
- Set `faction` to `2`.
- Keep `fallbackCombatRootTag` as `PlayerProjectileRoot`.

## Acceptance Criteria
- Bat, Skeleton, and Slime prefabs each have one wired `SkillDriver`.
- Mobs use mob faction and shared mob stat sheet.
- No new loadout assets are authored.
- Player prefab remains unchanged.

## Validation Required
- Search-confirm each prefab references the `SkillDriver` script GUID.
- Search-confirm each prefab references the selected loadout GUID and mob stat sheet GUID.
- Compile/build or report exact reason if unable.

## Hard Boundaries
- Do not edit code for this task.
- Do not create new skill/loadout assets.
- Do not modify runtime-built benchmark prefab path.
- Do not reopen architecture decisions.

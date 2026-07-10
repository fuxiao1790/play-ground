# Task Execution Packet

## Task
002-author-mob-loadout.md

## Goal
Choose an existing `SkillLoadout` asset for mobs. Do not create or edit loadout assets.

## Files Allowed To Modify
- `.agent/mob-skills/implementation-log.md`

## Files Allowed To Create
- `.agent/mob-skills/runtime/002-execution-packet.md`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/ScriptableObjects/Skills/Loadout/`
- `Assets/Prefabs/Player/Player.prefab`

## Behavior To Preserve
- Loadouts remain owner-agnostic.
- Player keeps using the same loadout asset.

## Behavior To Change
- Select one existing loadout for mob prefab wiring in task 003.

## Relevant Global Context
- `SkillDriver.faction` stamps owner faction at spawn time.
- The same loadout asset can be equipped by player and mobs.

## Dependencies Confirmed
- Existing loadout directory exists.
- `ProjectileStackTriggerLoadout.asset` exists with GUID `a2000000000000000000000000000005`.
- `Player.prefab` references the same loadout asset.

## Step-By-Step Instructions
- Pick an existing loadout under `Assets/ScriptableObjects/Skills/Loadout/`.
- Create no new `.asset` files.
- Feed the selected asset into task 003 prefab wiring.

## Acceptance Criteria
- No new `.asset` files are created for this feature.
- Chosen existing loadout remains equippable by player and mobs.

## Validation Required
- Search/list `Assets/ScriptableObjects/Skills/Loadout/`.
- Confirm selected GUID exists and no new loadout asset was created.

## Hard Boundaries
- Do not create or edit loadout assets.
- Do not add owner/faction typing to loadouts.
- Do not modify code for this task.

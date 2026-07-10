# Task Execution Packet

## Task
001-mobroot-drive-skilldriver.md

## Goal
Make `MobRoot` drive an optional `SkillDriver` every frame, firing at the first active enemy-faction target from existing target registries.

## Files Allowed To Modify
- `Assets/Scripts/Mob/MobRoot.cs`
- `.agent/mob-skills/implementation-log.md`

## Files Allowed To Create
- `.agent/mob-skills/runtime/001-execution-packet.md`

## Files Allowed To Delete
- None.

## Files Likely Needed For Reading
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`
- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Scripts/System/Core/CombatScope.cs`

## Behavior To Preserve
- Existing wander movement.
- Dead mob early return and proxy deletion.
- Mobs without `SkillDriver` run with no offense and no errors.
- Existing combat spawn/faction plumbing.

## Behavior To Change
- If a mob has `SkillDriver`, `MobRoot` ticks it after wander update.
- Active enemy targets cause firing toward their current position.
- No active enemy target still ticks cooldowns with `fireHeld=false`.
- `BindCombatRoot` forwards to `SkillDriver`.

## Relevant Global Context
- Use managed `CombatTargetRegistry<ICombatTarget>` lists, not ECS target spatial hash.
- `SkillDriver` owns cooldown ticking and spawn translation.
- Mob faction remains `CombatFaction.Mob`.

## Dependencies Confirmed
- `SkillDriver.Tick(bool, Vector2, Vector2)` exists.
- `SkillDriver.BindCombatRoot(CombatRoot)` exists and is idempotent.
- `CombatTargetRegistry<T>.Targets` exposes `IReadOnlyList<T>`.
- `ICombatTarget` exposes active state, position, and faction.
- `CombatFaction.Mob` value exists.

## Step-By-Step Instructions
- Add `using PlayGround.Skills;`.
- Add a private optional `SkillDriver` field.
- Resolve it in `Awake` with `GetComponent<SkillDriver>()`.
- Forward `BindCombatRoot` to `skillDriver?.BindCombatRoot(root)`.
- Call `DriveSkills()` after wander velocity is applied in alive `Update`.
- Implement `DriveSkills()` using first active enemy-faction target.
- Implement `TryAcquireEnemyTarget(out ICombatTarget enemy)` by scanning existing registries.

## Acceptance Criteria
- A wired mob fires its loadout toward an active enemy-faction target every cooldown period.
- A mob with no `SkillDriver` wanders normally with no errors.
- No new spawn/faction plumbing added.
- Only `MobRoot.cs` changes for code.

## Validation Required
- C# compile/build after implementation.
- Search/read verification of the changed code.

## Hard Boundaries
- Do not modify files outside the allowed list except context/log files required by the orchestrator.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks.
- Do not reopen index-level decisions.
- Stop on architectural ambiguity.

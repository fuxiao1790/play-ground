# Task Execution Packet

## Task
001-component-lifecycle-model.md

## Goal
Make timed spawning an enableable component state while keeping impact AOEs free of timed-spawn and lifetime components.

## Files Allowed To Modify
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `.agent/unify-combat-archetypes/implementation-log.md`

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`

## Behavior To Preserve
- Impact AOE archetype remains lifetime/timed-spawn absent.

## Behavior To Change
- `TimedSpawnComponent` implements `IEnableableComponent`.
- Lifecycle comments document timed-spawn enablement and lifetime invariant.

## Relevant Global Context
- Three archetypes: projectile, impact AOE, lingering AOE.
- Timed spawn is enabled state on projectile and lingering AOE only.

## Dependencies Confirmed
- None.

## Step-By-Step Instructions
- Change `TimedSpawnComponent` to implement `IEnableableComponent`.
- Update `TimedSpawnComponent`, `TimedSpawnStateComponent`, and `CombatLifetimeComponent` lifecycle comments.

## Acceptance Criteria
- `TimedSpawnComponent` enabled state is the runtime selector.
- Lifetime/timed-spawn presence rules are documented.

## Validation Required
- Compile later after dependent tasks.

## Hard Boundaries
- Do not modify impact AOE archetype in this task.
- Do not remove `TimedSpawnTag` before runtime references are updated.

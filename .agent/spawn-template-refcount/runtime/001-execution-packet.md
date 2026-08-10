# Task Execution Packet

## Task
001-registry-refcount-state.md

## Goal
Add scope-owned registry lifetime state and kind helper without changing command registry components.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/SpawnTemplateComponents.cs`
- `Assets/Scripts/System/Core/CombatScopeOwner.cs`

## Behavior To Preserve
- Existing three template-component map shapes and lookup behavior.

## Behavior To Change
- Scope now owns matching count maps and one delta queue.

## Relevant Global Context
- Persistent containers allocated/disposed by `CombatScopeOwner`; jobs will later queue deltas only.
- Impact and lingering AOE use one count map.

## Dependencies Confirmed
- None required.

## Step-By-Step Instructions
- Add refcount, delta, registry-state structs and AOE-kind helper.
- Allocate, attach, dispose, and clear count maps plus delta queue with existing registry maps.

## Acceptance Criteria
- Original template components byte-identical.
- State exists with registries and shares all scope teardown paths.
- Nothing reads state yet.

## Validation Required
- Static inspection only; Unity tests deferred.

## Hard Boundaries
- No other files or later-task behavior.

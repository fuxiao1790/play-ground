# Task Execution Packet

## Task

003-split-common.md

## Goal

Confirm and record the existing ownership partition of `Common` in preparation for later physical package moves. This task changes no source locations or namespaces.

## Files Allowed To Modify

- None.

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `Assets/Scripts/Common/DamageSnapshot.cs`
- `Assets/Scripts/Common/GameplayTags.cs`
- `Assets/Scripts/Common/GameplayLayers.cs`
- `Assets/Scripts/Common/Stats/`
- `Assets/Scripts/Common/StatusEffects/`
- `Assets/Scripts/Common/PersistentScriptableObject.cs`
- Simulation files that import `PlayGround.Common`.

## Behavior To Preserve

- All code, namespaces, runtime data shapes, and locations remain unchanged until package tasks 004 and 005.

## Behavior To Change

- None. Record the split classification only.

## Relevant Global Context

- Sim package will own `DamageSnapshot`, `GameplayTags`, and `GameplayLayers`.
- Game logic will own `Common/Stats`, `Common/StatusEffects`, and `PersistentScriptableObject`.
- Namespaces may span assemblies; do not rename them.

## Dependencies Confirmed

- Task 002 is complete and the Common-to-sim `AoeConfig` import has been removed.

## Step-By-Step Instructions

1. Confirm the three sim-shared files import no game-logic namespaces or types.
2. Confirm simulation uses only those three Common primitives.
3. Confirm no source change is necessary in this task.

## Acceptance Criteria

- The three sim-shared files reference no game-logic type.
- Game-logic Common files may retain legal downward references to sim contracts or primitives.

## Validation Required

- Use search-based evidence and report the classification.

## Hard Boundaries

- Do not modify, move, rename, or split source files. Physical moves belong exclusively to tasks 004 and 005.
- Do not change architecture or add abstractions.

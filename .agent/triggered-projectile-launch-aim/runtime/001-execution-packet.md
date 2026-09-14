# Task Execution Packet

## Task
001-refactor-combat-target-acquisition.md

## Goal
Move/rename `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` to a domain-neutral
`CombatTargetAcquisition` (new file location under a combat-neutral folder, e.g.
`Assets/Scripts/System/Combat/CombatTargetAcquisition.cs` — pick the smallest sensible
neutral location consistent with existing project folder conventions; do not invent a
deep new folder hierarchy). Keep its Burst-compatible `Snapshot`, nearest/Nth-nearest
selection, collision-shape range test, deduplication, faction filter, deterministic
ordering, and excluded-target support byte-for-byte identical in behavior. Update all
production and test callers to the new name/namespace/location.

## Files Allowed To Modify
- Assets/Scripts/System/Targeted/TargetedAcquisition.cs (moved/renamed — delete old path after move)
- Assets/Scripts/System/Targeted/TargetedResolveSystem.cs
- Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs
- Assets/Tests/EditMode/TargetedResolveEditModeTests.cs
- Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs
- Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs
- Any other file found by grep for `TargetedAcquisition` at execution time (report before modifying if it's outside this list)

## Files Allowed To Create
- New file for `CombatTargetAcquisition` (e.g. `Assets/Scripts/System/Combat/CombatTargetAcquisition.cs`)
- Matching `.meta` file is handled by Unity/user; do not hand-author Unity meta files — if a `.meta` exists for the old file, leave meta handling to the user (see hard boundaries).

## Files Allowed To Delete
- Assets/Scripts/System/Targeted/TargetedAcquisition.cs (after content is moved to new location)

## Files Likely Needed For Reading
- Assets/Scripts/System/Targeted/TargetedAcquisition.cs (current implementation)
- Assets/Scripts/System/Targeted/TargetedResolveSystem.cs
- Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs
- Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs (for Snapshot/type context only — do not modify)
- Assets/Tests/EditMode/TargetedResolveEditModeTests.cs
- Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs
- Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs

## Behavior To Preserve
- Exact nearest/Nth-nearest selection semantics, collision-shape range test, dedup, faction filter, deterministic ordering, excluded-target support.
- No managed references or allocations enter the helper.
- No projectile/targeted entity archetype changes.
- No behavior change to targeted gameplay.

## Behavior To Change
- Type name/namespace/file location only: `TargetedAcquisition` -> `CombatTargetAcquisition`, moved out of the `Targeted`-specific folder/namespace into a domain-neutral one.

## Relevant Global Context
- Refactor is prerequisite for task 004, which will call this helper from projectile expansion code without coupling projectile code to a "Targeted"-named/owned type.
- Do not add any projectile-specific logic in this task — this task is a pure rename/move plus caller updates. No new methods, no new overloads, no new fields.
- Game-logic/ECS boundary and Burst-compatibility invariants apply (see Global Invariants in implementation-context.md) but should already hold in the existing implementation; just carry them over unchanged.

## Dependencies Confirmed
- None required (task 001 has no dependencies). Confirmed via grep that exactly these files reference `TargetedAcquisition`/`ExternalSpawnGateSystem`:
  - Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs (check if this references the type itself or just related types — verify before editing)
  - Assets/Tests/EditMode/TargetedResolveEditModeTests.cs
  - Assets/Scripts/System/Targeted/TargetedResolveSystem.cs
  - Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs
  - Assets/Scripts/System/Targeted/TargetedAcquisition.cs (self)
  - Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs
  - Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs

## Step-By-Step Instructions
1. Read `Assets/Scripts/System/Targeted/TargetedAcquisition.cs` in full.
2. Determine a domain-neutral location/namespace matching existing project conventions (survey sibling folders like `Assets/Scripts/System/Api`, `Assets/Scripts/System/Core`, etc. for a "combat-neutral" home — task index calls the concept `CombatTargetAcquisition`, so a folder such as `Assets/Scripts/System/Combat/` is a reasonable candidate if it fits existing structure; do not create deep new hierarchies).
3. Create the new file with the type renamed to `CombatTargetAcquisition`, same members/signatures, same namespace-style convention as the new location dictates (match existing namespace patterns in the codebase).
4. Delete the old `TargetedAcquisition.cs` file.
5. Update every caller found via grep to use the new type name/namespace (using directives, fully-qualified references, etc.).
6. Update test files referencing the old type name.
7. Grep again for `TargetedAcquisition` across `Assets/` to confirm zero remaining references (aside from unrelated substring matches you must inspect individually).
8. Do not touch unrelated code in any updated file beyond the rename/reference change.

## Acceptance Criteria
- One production implementation owns nearest-hostile spatial-hash selection.
- Helper name/namespace no longer implies targeted-archetype ownership.
- Existing targeted root acquisition and chain selection use the renamed helper.
- Existing deterministic ordering, target-shape intersection, rank behavior, and exclusion behavior unchanged.
- No managed references or allocations enter helper.
- No projectile/targeted entity archetype changes.

## Validation Required
- Static/search-based verification: confirm zero remaining references to old type name in `Assets/`.
- Report whether a Unity compile check is possible from this environment; if not runnable, state so explicitly. Do not run Unity tests (user runs tests separately per project policy).

## Hard Boundaries
- Do not modify files outside the allowed list except for imports/namespaces directly required by this task.
- Do not change architecture.
- Do not introduce new abstractions not described by the task.
- Do not combine this task with later tasks (no enum, no trigger fields, no expansion-system changes — those are tasks 002-004).
- Do not reopen index-level decisions.
- Stop on architectural ambiguity (e.g., if the "right" neutral location is genuinely unclear/contentious, pick the smallest reasonable option and note it as a minor deviation rather than blocking — this is a local/naming ambiguity, not architectural).
- Do not hand-edit Unity `.meta` files.

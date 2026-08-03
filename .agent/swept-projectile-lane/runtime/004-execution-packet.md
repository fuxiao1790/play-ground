# Task Execution Packet

## Task

004-expansion-command-fanout.md

## Goal

Fan resolved projectile commands from one expansion job into discrete or swept command list using only command's authored integer flag.

## Files Allowed To Modify

- `Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- `ProjectileSpawnExpansionSystem.cs`
- `ProjectileSpawnPipeline.cs`

## Behavior To Preserve

- One event queue/event type, existing expansion stamping/volley logic, `PendingHandle` ownership.

## Behavior To Change

- Allocate/write/dispose separate `Commands` and `SweptCommands` lists.

## Relevant Global Context

- Event producers cannot know template sweep setting; expansion `WriteCommand` is single correct fan-out point.
- No speed/radius/timestep/config logic in this job. One expansion job owns both lists, so one pending handle.
- All TempJob containers dispose across no-event, no-template, normal, next-frame, and destroy paths.

## Dependencies Confirmed

- `ProjectileSpawnCommand.SweptCollision` exists as `int`.
- Existing singleton owns event queue and per-frame `Commands` list; expansion has all required cleanup/scheduling paths.

## Step-By-Step Instructions

1. Add `SweptCommands` and lifecycle documentation to singleton.
2. Dispose/reset it with `Commands` in previous-frame cleanup and OnDestroy.
3. Allocate swept list before scheduling normal expansion; assign it to job and singleton.
4. Route `command.SweptCollision != 0` into it; all other commands use discrete list.
5. Ensure early exits leave both lists safely default/uncreated.

## Acceptance Criteria

- One event queue remains.
- Both lists allocated/assigned/disposed every exit path.
- Routing only reads integer membership field.
- All resolved template paths converge through routing.

## Validation Required

- Static cleanup/path search and diff check; compile baseline may remain blocked.

## Hard Boundaries

- No second event lane, no new singleton, and no apply-system edits (task 005).

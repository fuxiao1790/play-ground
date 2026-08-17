# Task Execution Packet

## Task
`002-ids-to-spawn-commands.md`

## Goal
Register every nested definition's clip and carry only the resolved id/radius through every spawn command.

## Dependencies Confirmed
- Task 001 lane/helper types exist and compile.
- Existing recursive type/template walks identify all required definition edges.

## Hard Boundaries
- No `AudioClip` enters ECS or jobs; id `0` stays silent; Sim assembly references stay unchanged.

## Validation Required
- Sim/GameLogic Roslyn compiles, recursive registration EditMode source, hash/diff/static propagation checks.

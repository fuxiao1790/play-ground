# Task Execution Packet

## Task
`003-emit-at-expansion.md`

## Goal
Emit once per materialized spawn, defer armed-AOE sound to arm completion, and remove managed cast-site emission.

## Dependencies Confirmed
- All spawn commands carry `SoundIds` and `SpawnSoundRadius`.
- `SoundEmit` and `SoundEventSingleton` compile in Burst-facing Sim code.

## Hard Boundaries
- Combine producer handles on the main thread; never call `AudioRoot` from a job.
- Preserve AOE arming timing and avoid root double emission.

## Validation Required
- Sim/GameLogic Roslyn compiles, emitter/old-enqueue searches, diff check, and later in-player counter observation.

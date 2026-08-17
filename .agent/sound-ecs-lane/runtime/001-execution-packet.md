# Task Execution Packet

## Task
`001-sound-event-lane.md`

## Goal
Add a singleton-owned native sound lane, Burst enqueue helper, and transport-only presentation dispatcher.

## Dependencies Confirmed
- Shipped `SoundEvent` and `AudioRoot.Enqueue` exist and compile in `PlayGround.Sim`.

## Hard Boundaries
- Keep selection/playback in `AudioRoot`; keep `SoundEvent` and `AudioRoot` behavior unchanged.
- Own allocation, handle completion, clearing, and disposal in the dispatcher.

## Validation Required
- Unity Roslyn Sim compile, lifecycle inspection, diff check, and later profiler ordering capture.

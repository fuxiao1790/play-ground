# Task Execution Packet

## Task
006-spatial-hash-gather-job.md

## Goal
Move target snapshot copying to a scheduled Burst gather job.

## Dependencies Confirmed
- Task 004 consumer-handle registration is implemented.
- Target snapshot lists are resized main-thread and `CalculateAoeCapacity` currently reads their populated contents main-thread.

## Blocker
- The specified `GatherTargetsJob.Schedule()` populates snapshot lists asynchronously. The required retained main-thread `CalculateAoeCapacity(singleton.TargetShapes.AsArray())` cannot safely read them until `gatherHandle.Complete()`.
- The task explicitly rejects completing the gather for capacity and requires `CalculateAoeCapacity` to remain main-thread, while also requiring build jobs to depend on the gather.

## Required Decision
- Authorize one of: complete the gather before main-thread capacity calculation; move capacity calculation into a job/native output; or change the gather scheduling shape.

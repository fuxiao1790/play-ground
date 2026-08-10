# Task Execution Packet

## Task
004-pool-cleanup-decrement.md

## Goal
Emit key decrements before pool cleanup destroys disabled slots.

## Files Allowed To Modify
- `Assets/Scripts/System/CombatPoolCleanupSystem.cs`

## Behavior To Preserve
- Parallel trim, deletion count, and combat-stat accounting.

## Behavior To Change
- Trim job direct-reads refcount singleton in owner system and passes writer into both cleanup schedules.

## Relevant Global Context
- Emit `-1` for on-hit, timed, detonation fields only when component exists; default key helper skips enqueue.
- Scope teardown needs no decrements.

## Dependencies Confirmed
- 001 registry delta state exists; 003 defines shared emitter.

## Step-By-Step Instructions
- Add read-only component handles and parallel writer.
- Gate chunk arrays with `Has`; emit before ECB destroy.

## Acceptance Criteria
- Every materialized key gets one trim decrement; Burst parallel job and existing metrics unchanged.

## Validation Required
- Static inspection; Unity tests deferred.

## Hard Boundaries
- Do not change death or scope teardown paths.

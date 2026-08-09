# Task Execution Packet

## Task
004-external-spawn-gate-job.md

## Goal
Move mana debit, spawn routing, and targeted acquisition into a scheduled Burst job while registering spatial-hash consumption.

## Files Allowed To Modify
- `Assets/Scripts/System/Spawning/ExternalSpawnGateSystem.cs`
- `.agent/burst-onupdate-work/implementation-log.md`

## Behavior To Preserve
- Mana-less/stale casters succeed; cost clamps to zero; rejection payloads unchanged.
- Four spawn event buffers receive same data and requests clear once.
- Targeted path falls back to request position and acquisition flag zero.

## Behavior To Change
- Gate work occurs in `IJob`; spatial hash read depends on `BuildHandle` and publishes consumer handle.

## Relevant Global Context
- No managed objects in job. Use existing hash and rejection producer fields; no new handles. Unhandled enum must fail loudly without Burst string exceptions.

## Dependencies Confirmed
- Existing hash `ConsumerHandle` pattern and all source buffers/lane fields are present.

## Validation Required
- Static checks. User-run routing/collision Unity tests with XML required.

## Hard Boundaries
- Do not change system ordering, event routes, or bridge ownership.

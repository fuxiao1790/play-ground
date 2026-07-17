# Task Execution Packet

## Task
004-shared-emit-helper.md

## Goal
Add one Burst-safe `VfxEmit.Enqueue` helper that routes by decoded shape, populate `VfxTimingData` at AOE spawn, and update every VFX emit site to use both Basic and Timed queues.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoePulseVfxSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatArmingSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`
- `Assets/Scripts/System/Lifetime/CombatDeathUtility.cs`
- `.agent/vfx-data-shapes/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Vfx/VfxEmit.cs`

## Files Allowed To Delete
- None.

## Behavior To Preserve
- Existing Basic-only content emits as before.
- Producer handles combine into the same singleton field.
- Pulse slot/system remain present.

## Behavior To Change
- Emit sites route by `DecodeShape(vfxId)`.
- Timed-shaped ids enqueue `TimedVfxSpawnRequest` with authored duration and tick interval.
- AOE entities carry `VfxTimingData`.

## Relevant Global Context
- Timed timing comes from authored lifetime/tick values, not remaining lifetime.
- Emit sites must not branch on AOE category for shape choice.

## Dependencies Confirmed
- Task 001 added shape request structs and timing component.
- Task 003 added both queues and dispatch drains.

## Step-By-Step Instructions
- Create `VfxEmit.Enqueue`.
- Add `VfxTimingData` to impact and lingering AOE archetypes and write it on spawn/reuse.
- Update expansion, arming, lifetime, collision, and pulse emits to use both queue writers.

## Acceptance Criteria
- Compiles.
- Basic ids route to Basic queue.
- Timed ids route to Timed queue with authored timing.
- No emitter reads `CombatLifetimeComponent.Remaining` for duration.

## Validation Required
- Search for direct `new VfxSpawnRequest` emits after refactor.
- Run available compile validation or document external blockage.

## Hard Boundaries
- Do not add authoring selectors here.
- Do not remove pulse path.

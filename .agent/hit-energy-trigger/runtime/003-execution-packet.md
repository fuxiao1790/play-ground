# Task Execution Packet

## Task
003-convert-ecs-accumulator.md

## Goal
Replace target-local integer stack state, status processor, and managed progress with float hit-energy accumulation and activation.

## Files Allowed To Modify
- `Assets/Scripts/System/Targets/CombatTargetProxy.cs`
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Status/StatusProcessSystem.cs` plus `.meta` during rename
- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Scripts/System/Application/CombatApplyBridge.cs`
- Player and mob roots implementing combat-target progress collection
- Direct production references to `TargetStackEntry`, `StatusStackSnapshot`, or task-003 legacy feature vocabulary
- Directly affected EditMode tests needed for task-003 data shape; broader regression migration remains task 004
- `.agent/hit-energy-trigger/implementation-log.md`

## Files Allowed To Create
- `Assets/Scripts/System/Status/HitEnergyActivationSystem.cs` reusing old system meta GUID

## Files Allowed To Delete
- `Assets/Scripts/System/Status/StatusProcessSystem.cs` after GUID-preserving rename

## Files Likely Needed For Reading
- Current finalizer accumulation helpers and system ordering attributes.
- Current status processing job and native spawn singleton contracts.
- Target proxy creation/archetype and presentation bridge collection path.

## Behavior To Preserve
- Process-before-finalize order: current-update deposits activate next update.
- Target buffer internal capacity and 32-entry maximum.
- Per-target maximum 256 emitted activations/update.
- Existing projectile/AOE native spawn lanes and unique spawn-id behavior.
- `CombatTickResult.HitCount` accepted-hit semantics.
- Presentation remains non-authoritative.

## Behavior To Change
- Buffer becomes `TargetHitEnergy` keyed by `AccumulatorId` with float stored/required energy.
- Each accepted hit deposits exactly `EnergyPerHit` once and refreshes payload-owned requirement, expiry, and spawn.
- Activation count uses floor division; subtract only emitted cost, retaining fractional remainder and capped overflow.
- Processor and managed progress use HitEnergy vocabulary throughout.

## Relevant Global Context
- Finalizer owns deposit and expiry refresh.
- Activation system owns expiry, threshold consumption, and spawn emission.
- `RetentionSeconds <= 0` disables payload through `HitEnergyPayload.Enabled`.
- No damage scale, crit, hit count aggregation, area, chain falloff, or delta-time scaling of energy.
- No new event lane or structural-change path.

## Dependencies Confirmed
- Task 002 added unmanaged `HitEnergyPayload` and `HitEnergySpawn`.
- Direct projectile/AOE/targeted transport now carries `CombatHitPayload.HitEnergy`.
- Remaining legacy production references are isolated to task-003-owned accumulator/finalizer/system/progress files.

## Step-By-Step Instructions
1. Rename `TargetStackEntry` to `TargetHitEnergy` and replace fields with `AccumulatorId`, `StoredEnergy`, `EnergyRequired`, `ExpiresAt`, and `HitEnergySpawn`.
2. Update target proxy archetype and lifecycle comments; retain internal capacity and 32-entry bound.
3. Convert finalizer lookup/create/update helpers to hit energy. Add exactly payload `EnergyPerHit` per accepted hit; refresh requirement, expiry, and spawn.
4. Rename `StatusProcessSystem` file/class to `HitEnergyActivationSystem`, preserving `.meta` GUID and ordering.
5. Expire at `now >= ExpiresAt`; compute floor activations; cap total emissions per target/update at 256; subtract only emitted cost; clamp tiny negative residue safely; retain overflow/remainder.
6. Emit `HitEnergySpawn` through existing queues with existing unique-id scheme.
7. Replace `StatusStackSnapshot` with `HitEnergyProgress` and update bridge/player/mob APIs and collections consistently.
8. Do not alter `CombatTickResult.HitCount`.

## Acceptance Criteria
- One accepted hit adds exactly `EnergyPerHit`; one emitted activation consumes exactly `EnergyRequired`.
- Fractional remainder and capped overflow persist.
- New deposits activate next update only.
- Retention semantics, 32 entries, and 256 activations remain.
- No old stack/debuff/detonation/status symbol remains for feature in production.
- `CombatTickResult.HitCount` unchanged.

## Validation Required
- User deferred full validation until all tasks finish.
- Perform static symbol/code-flow inspection and `git diff --check` only.
- Do not run Unity tests or Unity test runner.

## Hard Boundaries
- Do not modify files outside direct task-003 feature consumers except required imports/compile fixes.
- Do not add managed access or allocations to Burst hot paths.
- Do not add new queues, event lanes, or structural changes.
- Do not change unrelated combat-hit semantics.
- Do not implement task 004 regression suite or task 005 docs/assets.
- Stop on architectural ambiguity.

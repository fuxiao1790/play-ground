---
name: generalize-hit-cap-constant
description: Rename CollisionConstants.MaxAoeTargetsPerTick to the generic MaxHitsPerTick, no behavior change.
---

# 001 - Generalize the Hit Cap Constant

## Scope

Pure rename. No logic changes, no value changes. Prepares `CollisionConstants`
to be the shared home for the cap before task 004 adds a second consumer.
Independent of tasks 002/003 (the new lookup type and the AOE migration don't
reference this constant) — can land in any order relative to them, but must
land before 004.

## Changes

1. [CollisionConstants.cs:21](../../Assets/Scripts/System/Api/Collision/CollisionConstants.cs#L21)
   — rename `MaxAoeTargetsPerTick` to `MaxHitsPerTick`. Update the doc comment
   above it (currently "Hard-coded per-tick hit cap for AOE collision...") to
   describe it as the generic per-tick cap on accepted hits from a single
   spatial-hash lookup, still noting it's a safety bound and not a gameplay
   knob.
2. [AoeCollisionCore.cs:67](../../Assets/Scripts/System/Aoes/AoeCollisionCore.cs#L67)
   — update reference.
3. [ImpactAoeCollisionSystem.cs:176](../../Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs#L176)
   — update reference (`seenTargetKeys` allocation size).
4. [LingeringAoeCollisionSystem.cs:189](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L189)
   — update reference (`seenTargetKeys` allocation size).
5. [AoeSimulationTests.cs:425,435,443,453,456](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L425)
   — update all `CollisionConstants.MaxAoeTargetsPerTick` references. Leave
   test method names (`PulseHitsAtMostMaxAoeTargetsPerTick`,
   `PulseOverflowHitsFirstTargetsInCellScanOrder`) unchanged — they name
   AOE-domain behavior ("Pulse"), not the constant.

## Acceptance Criteria

- No remaining references to `MaxAoeTargetsPerTick` anywhere in the repo.
- `CollisionConstants.MaxHitsPerTick` still equals `32`.
- Existing AOE PlayMode tests pass unmodified in assertion logic (only the
  symbol name changes).

## Dependencies

None. This is a prerequisite for 004.

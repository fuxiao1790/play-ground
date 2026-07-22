# Task Execution Packet

## Task

004-tests-and-docs.md

## Goal

Update travel-spawn test vocabulary, add cost-driven energy cadence coverage, and document the energy model.

## Files Allowed To Modify

- `Assets/Tests/EditMode/SkillValidationEditModeTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs` (direct compile/semantic consumer of changed public `ProjectileChildSpawnConfig`)
- `Docs/reference/game-logic/skill-system.md`
- `Docs/reference/simulation/spawn-template-registry.md`
- Other documentation files only when a search finds a travel timed-spawn interval/cooldown description that must change.

## Behavior To Preserve

- Existing non-travel interval concepts (AOE ticks, tracking, hit cooldowns) remain interval-based.
- Existing arm/enable/disable test intent, template hashing boundary, and legacy request test purpose remain.

## Behavior To Change

- Test helpers/configs use rate, threshold/cost, and threshold jitter.
- Add a play-mode cadence test: equal rate and costs 1 versus 2 produce roughly 2:1; rate zero produces none.
- Docs describe trigger rate, child cost, threshold jitter, empty energy start, bounded per-tick accrual, and exclusion from template hashes.

## Relevant Global Context

- 001–003 completed. No interval fields remain in timed-spawn runtime data.
- Threshold jitter hashes source ID for each next tick; timing remains per-source and not template content.
- `EnergyThreshold` floor is `1e-3f`; rate <= 0 disables emission.

## Dependencies Confirmed

- Producers/consumer simulation now use energy fields, and legacy `ChildSpawn` remains supported through energy data.

## Acceptance Criteria

- Relevant edit/play tests compile with energy fields; cadence/disabled-rate behavior has coverage; docs have no travel-spawn interval/cooldown wording.

## Validation Required

- Run static checks and relevant tests if the Unity project is available. Current batch Unity compile is unavailable because another editor instance has the project open; state this precisely if still true.

## Hard Boundaries

- No Unity asset/YAML edits; task 005 remains user editor work. Do not alter non-travel interval terminology or unrelated docs/tests.

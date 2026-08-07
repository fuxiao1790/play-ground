# Task Execution Packet

## Task

002-targeted-components-and-contracts.md

## Goal

Amend completed task 002 for latest exact `TargetedSpawnCommand` shape. Preserve all prior targeted data, VFX, registry, and enum work.

## Files Allowed To Modify

- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`
- `Assets/Tests/EditMode/TargetedContractsEditModeTests.cs`

## Files Allowed To Create

- No new production files. Directly related test updates only.

## Files Allowed To Delete

- None.

## Behavior To Preserve

- Existing task-002 contracts, enum guards, registry lifecycle, and all projectile/AOE behavior.

## Behavior To Change

- Keep `JitterSeed` and `DeterministicIdTickIndex` beside command identity; template values default and expansion stamps event values.
- Replace flat `Lifetime`, `ArmSeconds`, `AcquireRadius`, `ChainRadius`, `ChainDamageFalloff`, `ChainDelaySeconds`, `MaxTargets` fields with `LifetimeSeconds`, `ArmSeconds`, and `TargetedResolveConfig Resolve`.
- Preserve VFX, rendering, payload, and timed-spawn fields exactly as current task 002 specifies.

## Relevant Global Context

- Command frame is event-stamped. `AimDirection` and `ContactGateSeedTargetId` stay event-only.

## Dependencies Confirmed

- Existing targeted contracts exist. Task 003 is pending this exact command shape.

## Step-By-Step Instructions

1. Align command field names/nesting exactly with current task 002 embedded definition.
2. Update `TargetedVfxUtility.TimingFor` to `LifetimeSeconds`.
3. Update contract tests for exact nested resolve and deterministic frame fields.

## Acceptance Criteria

- Existing behavior compiles/tests unchanged where runnable.
- Targeted command has exact nested resolve and both deterministic frame fields.
- Timing test uses `LifetimeSeconds`; no event-only field leaks into command.

## Validation Required

- Relevant EditMode tests and project compile check where available.
- Static command-shape audit and focused EditMode tests where available.

## Hard Boundaries

- Do not implement systems, routing bodies, authoring, compiler, or trigger work from later tasks.
- No spawn lane refactor.
- Do not edit Unity assets/YAML.

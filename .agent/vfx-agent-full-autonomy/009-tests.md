# 009 - EditMode and Command-Contract Tests

## Goal

Verify behavior only through public AgentVFX/command surfaces. Tests never
target production `Assets/Vfx/` content.

## Dependencies

001-008.

## Files

- Add `Assets/Tests/EditMode/AgentVfxExecCompatibilityTests.cs` rather than
  overloading existing compatibility class.
- Extend `AgentVfxCompatibilityTests.cs` for unified slot codec and complete
  graph snapshot.
- Add `AgentVFX.Commands` and `Unity.Pipeline` test-assembly references only if
  needed for direct command/registry binding tests.

## Required Tests

`AgentVfxCompatibilityTests`:

- `CanReadCompleteGraphSnapshot`
- `CanReadNestedSlots`
- `CanReadFlowConnections`
- `CanReadBlockOwnershipOrder`
- `CanIdentifySharedContextData`
- `CanRoundTripAnimationCurveSlot`
- `CanRoundTripTextureAssetSlot`

`AgentVfxExecCompatibilityTests`:

- `CanFindAllowedInternalTypes`
- `RejectsTypeOutsideReflectionPolicy`
- `CanDescribeKnownNonPublicMember`
- `CanDescribeScopedObject`
- `RejectsCrossGraphObjectHandle`
- `CanExecGetEnumerateAndReuseModelIds`
- `CanExecReferenceLocalResultInArgument`
- `CanExecCastWithDeclaredType`
- `CanExecInstanceMethodWithExactOverload`
- `CanExecCreateTransientValue`
- `CanExecCreateAndAttachVfxModel`
- `CanPerformFlowEditViaExecutor`
- `StopsAtFailureWithoutSaving`
- `ReportsPartialMutationAfterFailure`
- `SavesOnlyAfterSuccessfulBatch`
- `RejectsStaticCall`
- `RejectsOperationAndEnumerationLimits`

Command contract:

- `CanBindStructuredInternalExecRequest`
- `CanCreateTopLevelNodeUsingGraphSentinel`
- `ListsInternalVfxCommands`

## Test Rules

- Use unique assets under `Assets/AgentGenerated/__AgentVfx...__` and delete
  them in teardown through `AssetDatabase`.
- Copy/import real fixture shapes; never mutate
  `Assets/AgentGenerated/TestFixtures/MagicBoltTrail.vfx` in place when a test
  changes topology.
- Core acceptance tests must select deterministic known package types/fixture
  nodes. Do not hide regressions behind `Assert.Ignore` probes.
- Test public results, persisted snapshots, and command binding; do not add
  production test hooks for internal maps/codecs.

## User-Run Barrier

Agent writes but does not run tests. Ask user to run EditMode classes:

- `PlayGround.Tests.EditMode.AgentVfxCompatibilityTests`
- `PlayGround.Tests.EditMode.AgentVfxExecCompatibilityTests`

Export result to `Logs/TestResults-EditMode-VfxAgent.xml`. Review XML before
claiming pass or finalizing capability documentation.

## Acceptance Criteria

- Every required method exists under named EditMode classes and compiles.
- Tests use only public AgentVFX/command surfaces and scratch assets.
- User receives exact class names and XML output path; agent does not invoke
  Unity Test Runner.
- Capability status remains unverified until exported XML is reviewed.

## Scope

Large in line count; medium design risk.

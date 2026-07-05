# Task Execution Packet

## Task
006-docs-and-tests.md

## Goal
Update docs and tests for split event types and expansion systems, including a routing assertion.

## Files Modified
- Assets/Tests/EditMode/ProjectileAuthoringEditModeTests.cs
- Assets/Tests/PlayMode/AoePlayModeTests.cs
- Assets/Tests/PlayMode/AoeSimulationTests.cs
- Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs
- Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs
- Assets/Tests/PlayMode/SpawnCommandUnificationTests.cs
- Docs/*

## Acceptance Result
- Complete, except runtime test execution could not be completed in this session.

## Validation
- Static search confirms old exact names are gone from scripts/tests.
- `dotnet build` could not validate Unity assemblies because Unity package projects fail under normal SDK build before project code compile.

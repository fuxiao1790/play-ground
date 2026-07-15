# Implementation Log

## Status
In progress

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-promote-payload-component.md | Complete | `CombatHitPayload` is an ECS component on projectile/impact/lingering archetypes; spawn apply writes standalone payload; hit components slimmed. |
| 002-reshape-hit-event.md | Complete | `CombatHitEvent` now has `Source` and `Target`. |
| 003-producers-set-source.md | Complete | Projectile and AOE collision enqueue source/target and gate from source payload. |
| 004-finalize-source-lookup.md | Complete | Finalize uses read-only `ComponentLookup<CombatHitPayload>` from `hit.Source`. |
| 005-tests-and-doc.md | Complete | Direct hit tests create source entities; entity-constructing tests include standalone payload; contract doc updated. |

## Completed Tasks
- 001-promote-payload-component.md
- 002-reshape-hit-event.md
- 003-producers-set-source.md
- 004-finalize-source-lookup.md
- 005-tests-and-doc.md

## Blockers
- None

## Validation Summary
- Search validation for runtime old event fields completed.
- `dotnet build .\PlayGround.Runtime.csproj --no-restore /p:BuildProjectReferences=false` passed.
- `dotnet build .\PlayGround.Tests.PlayMode.csproj --no-restore /p:BuildProjectReferences=false` passed.
- `dotnet build .\PlayGround.Tests.EditMode.csproj --no-restore /p:BuildProjectReferences=false` passed.
- Full `dotnet build` is blocked before project code by existing package errors in `Library\PackageCache\com.unity.render-pipelines.core@6f62546dd936\Runtime\RenderGraph\Compiler\PassesData.cs`.

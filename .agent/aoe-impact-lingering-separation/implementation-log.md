# Implementation Log

## Status
Complete except optional cleanup skipped

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-variant-discriminator-and-events.md | Complete | Added split event structs, enum cases, scope buffers, and AOE variant helpers. |
| 002-classify-at-authoring.md | Complete | Authoring/builders classify AOE variant by lifetime. |
| 003-route-producers.md | Complete | Producers route to impact or lingering AOE event queues and chain both producer handles. |
| 004-split-expansion-systems.md | Complete | Added shared `AoeExpansionCore` and split expansion systems/command lists. |
| 005-dedup-collision-and-apply.md | Skipped | Optional cleanup; left to avoid widening the behavior change. |
| 006-docs-and-tests.md | Complete | Tests and docs updated; added split-lane routing assertion. |

## Completed Tasks
- 001-variant-discriminator-and-events.md
- 002-classify-at-authoring.md
- 003-route-producers.md
- 004-split-expansion-systems.md
- 006-docs-and-tests.md

## Blockers
- Full Unity validation blocked by existing Unity editor processes and generated-project package build failures under `dotnet`.

## Validation Summary
- Static search: no exact `AoeSpawnEvent`, `AoeSpawnExpansionSystem`, `IntervalChildKind.Aoe`, or `StackDetonationKind.Aoe` references remain in `Assets/Scripts` or `Assets/Tests`.
- Static search: no old exact names remain in docs except valid file-path mentions of `AoeSpawnExpansionSystem.cs`.
- `git diff --check` passed with line-ending warnings only.
- `dotnet build PlayGround.Runtime.csproj` failed in Unity package cache before project code compile: `Unity.RenderPipelines.Core.Runtime.csproj` CS8168/CS8347.
- `dotnet build PlayGround.Runtime.csproj --no-dependencies` failed because generated Unity dependency DLLs were missing in `Temp/bin/Debug`.

# Implementation Log

## Status

Blocked — awaiting user Unity Editor asset/catalog migration.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-onhittrigger-collapse.md | Complete | Added unified compiler/validator path; landed with 003 source migration. |
| 002-narrow-onhit-aoe-field.md | Complete | Narrowed AOE field and simplified two driver guards. |
| 003-test-migration.md | Complete | Migrated all deleted trigger references; added two EditMode cases. |
| 004-asset-and-catalog-migration.md | Blocked | User-performed Unity Editor work; no `.asset` or `.meta` YAML may be hand-edited. |
| 005-doc-updates.md | Complete | Unified on-hit and interval ownership documentation; legacy trigger-name searches pass. |
| 006-interval-trigger-attribute-removal.md | Complete | Removed interval overrides/setups and migrated affected EditMode/PlayMode tests. |
| Follow-up: on-hit projectile nova parity | Complete | On-hit projectile bursts now use interval side-spray templates and expansion; added collision-pipeline regression coverage. |

## Completed Tasks

- 001-onhittrigger-collapse.md
- 002-narrow-onhit-aoe-field.md
- 003-test-migration.md
- 006-interval-trigger-attribute-removal.md
- 005-doc-updates.md

## Blockers

- 004-asset-and-catalog-migration.md requires Unity Editor asset creation, catalog editing, and deletion; project convention forbids hand-editing `.asset` or `.meta` YAML.

## Validation Summary

- Static searches confirm no deleted trigger types remain in `Assets/Scripts` or `Assets/Tests`, no old AOE type guard remains, and no interval trigger attribute use remains.
- `git diff --check` passes.
- Unity headless compilation exited 0; no tests ran.
- `dotnet build PlayGround.GameLogic.csproj --no-restore` is not usable for this Unity project: it fails in unchanged package source `PassesData.cs` with CS8168 and CS8347 before project code compiles.
- Unity test execution remains user-owned. Required XML: `Logs/TestResults-EditMode.xml`, `Logs/TestResults-PlayMode.xml`.

# Task Execution Packet

## Task
006-verify.md

## Goal
Run final static verification: GUIDs, stale references, ResourceBars naming, final tree, and folder absence.

## Files Allowed To Modify
- None.

## Files Allowed To Create/Delete
- None.

## Files Likely Needed For Reading
- All moved file metas.
- `Assets/Scripts/Ui/PlayGround.Ui.asmdef`.
- `Assets/Scenes/BenchmarkLarge.unity` and project source/assets for stale-token classification.

## Behavior To Preserve
- No changes; verification only.

## Relevant Global Context
- Tasks 001–005 complete. Unity tests/builds are deferred to user.

## Dependencies Confirmed
- Final docs and destination tree exist; old folders absent.

## Step-By-Step Instructions
1. Check every moved meta against plan GUIDs.
2. Scan repo excluding `.git` for old assembly/path tokens; classify expected editor cosmetics and plan-history references.
3. Scan `ResourceBarsUi` and require zero hits in live project files.
4. Print and compare final `Ui` tree.
5. Update implementation log and report Unity editor regeneration/console checks for user.

## Acceptance Criteria
- All recorded moved GUIDs unchanged.
- No stale live source/docs references; no `ResourceBarsUi` live hits.
- Final tree matches task specification.
- Both old folders absent.

## Validation Required
- Static scans and tree listing only. No Unity test/build.

## Hard Boundaries
- No source/doc changes in verification task.
- Do not edit generated `.slnx`/`.csproj` files.

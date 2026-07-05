# Task Execution Packet

## Task
006-verify-and-cleanup.md

## Goal
Verify the five combat lanes no longer use cross-system native-container reach-ins, update docs with the landed singleton examples, and record validation limits.

## Files Allowed To Modify
- `Docs/coding-standards.md`
- `.agent/combat-event-singletons/implementation-log.md`
- Source files only if verification finds a directly related cleanup issue.

## Files Allowed To Create
- None

## Files Allowed To Delete
- None

## Files Likely Needed For Reading
- All migrated systems and producers.

## Behavior To Preserve
- No runtime behavior changes beyond the storage/access relocation already completed.

## Behavior To Change
- Coding standard cites the real landed singleton examples.

## Relevant Global Context
- Managed presentation bridge lookups are outside native-container lane migration.
- Manual handles in singleton structs are intentional.

## Dependencies Confirmed
- Tasks 001 through 005 are complete by grep evidence.

## Step-By-Step Instructions
- Grep all five migrated sink lookups.
- Grep old internal native lane fields and writer helpers.
- Confirm every singleton declaration has an `ECS Lifecycle:` comment.
- Add docs cross-link/examples in `Docs/coding-standards.md`.
- Attempt build/test validation and record exact result.

## Acceptance Criteria
- Static checks pass or retained managed lookups are justified.
- Docs reference landed singleton examples.
- Validation status is explicit.

## Validation Required
- Search-based verification.
- Build/test attempt if available.

## Hard Boundaries
- Do not reopen architecture or add new data paths.
- Do not run profiler/manual play checks; report not run if not available in this session.

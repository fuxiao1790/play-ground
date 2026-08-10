# Task Execution Packet

## Task
007-docs-and-tests.md

## Goal
Document refcount lifetime and add/fix test coverage and fixtures for direct scope singleton reads.

## Files Allowed To Modify
- `Docs/reference/simulation/spawn-template-registry.md`
- `Docs/contracts/combat-root-api.md`
- `Docs/reference/game-logic/skill-system.md`
- Test fixtures named by task and `Assets/Tests/PlayMode/AoePlayModeTests.cs` or new focused test file.

## Behavior To Preserve
- Fail-loud singleton reads; no test-only production metadata.

## Behavior To Change
- Test worlds that run affected systems construct a combat scope. Tests cover owner, instance, pin, pooling, and SkillDriver lifecycle.

## Relevant Global Context
- Registry mutation is managed pre-tick or late sweep only; sweep preserves tick-long read-only map contract.
- Tests are written but never run by agent; XML user results required.

## Dependencies Confirmed
- 001–006 complete: scope state, owner API, apply/trim deltas, sweep, driver release.

## Step-By-Step Instructions
- Update concurrency, lifetime, API, skill lifecycle docs.
- Audit named test-world fixtures and add state via normal scope setup.
- Add nine outlined PlayMode cases.

## Acceptance Criteria
- Docs correct; test fixtures retain fail-loud behavior; count-drift regression covered.

## Validation Required
- Static inspection only. User-run PlayMode/EditMode commands export XML.

## Hard Boundaries
- Do not weaken production singleton reads or run Unity tests.

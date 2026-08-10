# Task Execution Packet

## Task
006-skilldriver-owner-lifecycle.md

## Goal
Keep each `SkillDriver` registration-key set and release prior/root-replaced/destroyed claims.

## Files Allowed To Modify
- `Assets/Scripts/Skills/SkillDriver.cs`

## Behavior To Preserve
- Registration recursion and produced key kinds; same-root bind no-op.

## Behavior To Change
- Registration is new-first, old-release-second. Driver teardown/rebind releases against old root.

## Relevant Global Context
- One list entry per existing registration call. Unregister is safe during root teardown.

## Dependencies Confirmed
- 002: `CombatRoot.UnregisterSpawnTemplate` accepts kind/key safely.

## Step-By-Step Instructions
- Add key list.
- Record each returned key at registration sites.
- Swap lists on register pass and release prior set after registration.
- Release on destroy and before different-root bind.

## Acceptance Criteria
- No duplicate release; unchanged recompile does not churn shared entries; in-flight entities retain keys by instance count.

## Validation Required
- Static inspection; Unity tests deferred.

## Hard Boundaries
- No compile or spawn behavior redesign.

# Task Execution Packet

## Task
002-combatroot-register-unregister-api.md

## Goal
Make managed template registrations increment owner counts, add teardown-safe unregister, and pin ad-hoc spawn registrations.

## Files Allowed To Modify
- `Assets/Scripts/System/Core/CombatRoot.cs`

## Behavior To Preserve
- Content hashes, command normalization, and template deduplication.

## Behavior To Change
- Register gains a managed owner claim; private pinned registration serves unmanaged spawn paths; unregister only drops a claim.

## Relevant Global Context
- Count maps live in direct scope singleton state. AOE kinds share one count map. No erase from `CombatRoot`.

## Dependencies Confirmed
- 001: `SpawnTemplateRegistryState` and count-map state exist on scope acquire.

## Step-By-Step Instructions
- Add `AddOwner` and `CountsFor` helpers.
- Update three public registrations.
- Add safe unregister.
- Use pinned private registration for two ad-hoc `Spawn` overloads.

## Acceptance Criteria
- Duplicate registrations make one command entry and increment count.
- Unknown/default unregister no-ops and never erases.
- Repeated identical ad-hoc spawn has one pinned entry.

## Validation Required
- Static inspection; Unity tests deferred.

## Hard Boundaries
- Do not implement apply, cleanup, sweep, or SkillDriver work.

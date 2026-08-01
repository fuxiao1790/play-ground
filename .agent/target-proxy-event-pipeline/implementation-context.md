# Implementation Context

## Architectural Decisions
- Managed target-proxy lifecycle uses scope-buffer events and ECS apply systems only.
- Create allocation is deferred; `CombatTargetProxy` correlates create via its own token.
- `TargetId` is not a create correlator. `TargetKey(Entity)` remains ECS identity.

## Global Invariants
- Event payloads are unmanaged; managed target references remain in `CombatTargetProxy` bookkeeping.
- Create/update apply before `TargetSpatialHashSystem`; delete applies in Presentation after `CombatApplyBridge`.
- Update events share one FIFO buffer so resource-max updates precede health/mana clamping.

## Ownership / Data Flow
- Actor roots enqueue proxy intent on shared `CombatScopeOwner` scope entity.
- ECS owns live proxy state; presentation bridge alone reads `TargetCompanion`.

## Lifecycle / ECS Constraints
- No direct managed `EntityManager` proxy mutation after migration.
- No jobs read managed companions. Target lifecycle structural changes are main-thread apply work.

## Reused / Introduced Mechanisms
- Reuse existing scope entity and projectile/AOE event-to-apply pattern.
- Add create/update/delete proxy events and matching apply systems.

## Validation Requirements
- Validate each task before continuing; run scoped Unity checks where task specifies them.
- Preserve unrelated work, including untracked `.claude/settings.local.json`.

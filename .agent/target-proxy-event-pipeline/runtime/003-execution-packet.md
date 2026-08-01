# Task Execution Packet

## Task

003-apply-systems.md

## Goal

Add main-thread create/update/delete proxy apply systems over all combat scopes.

## Files Allowed To Create

- `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyUpdateApplySystem.cs`
- `Assets/Scripts/System/Targets/TargetProxyDeleteApplySystem.cs`

## Dependencies Confirmed

- Scope buffers and all event shapes exist.
- `CombatTargetProxy.TryTakePendingCreate`, cancellation state, and cached `Archetype(EntityManager)` exist.

## Behavior

- Create consumes tokens, creates complete proxy archetype, writes companion, then target entity.
- Update drains FIFO sequence and clamps current after max changes.
- Delete is Presentation after `CombatApplyBridge` and destroys existing proxies.

## Ordering

- Create/Update Simulation, before `TargetSpatialHashSystem`; Create before Update.
- Delete Presentation, after `CombatApplyBridge`.

## Validation

- Static ordering and event-drain inspection. Build/tests handled later if unavailable now.

## Hard Boundaries

- Do not modify roots, registry, skill driver, tests, or docs.

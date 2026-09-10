# Task Execution Packet

## Task

002-narrow-onhit-aoe-field.md

## Goal

Narrow `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` to `RuntimeAoeDefinition` and remove redundant runtime type guards.

## Files Allowed To Modify

- `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`

## Files Allowed To Create

- None.

## Files Allowed To Delete

- None.

## Files Likely Needed For Reading

- Runtime AOE/targeted definitions; all `OnHitAoeSpawnDefinition` consumers; compiler attachment helper.

## Behavior To Preserve

- Projectile, AOE, targeted on-hit priority and resulting `OnHitSpawnRef` shapes.

## Behavior To Change

- Field declaration becomes `RuntimeAoeDefinition`; consumers use null checks.

## Relevant Global Context

- Task 001's `AttachOnHitTarget` only assigns this field when target is `RuntimeAoeDefinition`.

## Dependencies Confirmed

- `OnHitTrigger` and AOE-target attachment arm exist.

## Step-By-Step Instructions

1. Narrow field type and replace its trigger comment.
2. Simplify exactly two `BuildOnHitSpawnRef` guards without reordering priority.
3. Inspect other call sites; do not add casts.

## Acceptance Criteria

- No `OnHitAoeSpawnDefinition is RuntimeAoeDefinition` pattern remains; both overloads retain projectile → AOE → targeted order.

## Validation Required

- Static source/search verification now. Unity compilation is deferred to user.

## Hard Boundaries

- No template, field, or spawn-path redesign; no priority reordering.

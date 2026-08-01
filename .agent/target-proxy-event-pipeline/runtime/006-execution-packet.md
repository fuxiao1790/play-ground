# Task Execution Packet

## Task

006-actor-root-call-sites.md

## Goal

Bind SkillDriver to actor owners and make proxy deletion cancel pending creation reliably.

## Files Allowed To Modify

- `Assets/Scripts/Player/PlayerRoot.cs`
- `Assets/Scripts/Mob/MobRoot.cs`

## Dependencies Confirmed

- `CombatTargetProxy.Delete` cancels pending create.
- `SkillDriver.BindCaster` takes `ICombatTarget`.

## Required Behavior

- `Register`: bind `this`, set `hasRegisteredProxy` immediately after registry registration.
- All Player and Mob normal/SoftDie deletion guards use that flag, not live entity non-nullness.

## Validation

- Scan all `BindCaster` and deletion paths; diff check.

## Hard Boundary

- No tests/docs/registry changes.

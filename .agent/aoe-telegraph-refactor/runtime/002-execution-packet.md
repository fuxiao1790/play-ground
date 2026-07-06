# Task Execution Packet

## Task
002-delete-dead-oneshot-path.md

## Goal
Delete unreachable lifetime enable-bit reads from lingering AOE collision and pulse VFX.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`
- `.agent/aoe-telegraph-refactor/implementation-log.md`

## Dependencies Confirmed
- `LingeringAoeCollisionSystem` query uses `WithAll<LingeringAoeTag>`.
- Search found only `true` writes to `CombatLifetimeComponent` enable state.

## Step-By-Step Instructions
- Remove `EnabledRefRO<CombatLifetimeComponent>` from lingering collision and pulse VFX jobs.
- Remove the `WithPresent<CombatLifetimeComponent>` lingering job attribute and dead comment.
- Always tick `AoeHitGateComponent`.
- Pass `false` for lingering collision `deactivateAfterPass`.
- Keep `ref AoePulseVfxComponent` so pulse VFX remains lingering-only.

## Acceptance Criteria
- No `EnabledRefRO/RW<CombatLifetimeComponent>` remains in AOE systems.
- Lingering and impact behavior unchanged.

## Validation Required
- Search checks after patch.
- Build/test or explain if unavailable.

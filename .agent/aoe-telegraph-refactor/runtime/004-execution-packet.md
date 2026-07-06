# Task Execution Packet

## Task
004-docs-and-comments.md

## Goal
Refresh comments and docs for named AOE discriminator plus plain lifetime timer.

## Files Allowed To Modify
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Docs/reference/simulation/ecs-notes.md`
- Other `Docs/` files with stale lifetime discriminator or dead enable-bit wording found by grep.
- `.agent/aoe-telegraph-refactor/implementation-log.md`

## Dependencies Confirmed
- 001 introduced `LingeringAoeTag`.
- 002 removed dead lifetime enable-bit one-shot reads.
- 003 made `CombatLifetimeComponent` plain data.

## Step-By-Step Instructions
- Ensure `LingeringAoeTag` comment states it is the discriminator.
- Ensure `CombatLifetimeComponent` comment says plain timer and not enableable/discriminator.
- Update simulation docs that mention lifetime presence routing or disabled lifetime pulse one-shot state.
- Add windup breadcrumb to `ecs-notes.md`.

## Acceptance Criteria
- No doc/comment describes lifetime presence as the AOE discriminator.
- No doc/comment references pulse-one-shot lifetime enable-bit state.
- `ecs-notes.md` reflects named discriminator, plain lifetime, and future windup breadcrumb.

## Validation Required
- Grep `Docs/` and relevant comments after patch.

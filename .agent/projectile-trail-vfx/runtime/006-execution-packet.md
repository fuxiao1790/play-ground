# Task Execution Packet

## Task
`006-docs-updates.md`

## Goal
Correct projectile/VFX docs to describe optional, skill-authored line-segment trails.

## Files Allowed To Modify
- `Docs/contracts/vfx-requests.md`
- `Docs/reference/simulation/vfx-system.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/game-logic/skill-system.md` only if its authoring section enumerates relevant slots.

## Behavior To Preserve
- AOE systems do not emit `LineSegment` VFX.
- Targeted chains remain a `LineSegment` producer.

## Behavior To Change
- Document `ProjectileMovementSystem` as second `LineSegment` producer, once per active non-arming trailed projectile/tick.
- Explain trail is optional per `BasicAttackPrefab` and registered by `SkillDriver`.

## Dependencies Confirmed
- Code contains authoring fields, runtime/command/component data flow, and movement queue emission.

## Acceptance Criteria
- No covered doc says projectiles never emit VFX/line segments.
- Covered VFX docs identify exactly targeted links and projectile trails as line-segment producers.

## Validation Required
- Static docs/source search only. Unity tests deferred to user.

## Hard Boundaries
- Documentation only; no design changes.

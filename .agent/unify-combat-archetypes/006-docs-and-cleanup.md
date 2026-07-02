# 006 Docs And Cleanup

## Goal

Make docs match the new lifecycle and remove stale archetype language.

## Scope

- Update docs:
  - `Docs/reference/simulation/projectile-system.md`
  - `Docs/reference/simulation/aoe-system.md`
  - `Docs/reference/simulation/spawn-template-registry.md`
  - `Docs/reference/simulation/project-aoe-system-common.md`
  - `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`
- Replace wording that says timed-spawner projectiles/AOEs are separate archetypes.
- Replace wording that says impact AOEs lack `CombatLifetimeComponent`.
- Document memory tradeoff: fewer archetypes and simpler reuse at cost of wider base chunks.
- Remove dead code:
  - `TimedSpawnTag` if no compatibility need remains
  - dead apply systems/buckets
  - stale profiler counters or update ordering attributes.

## Acceptance Criteria

- Docs and lifecycle comments describe one projectile archetype and one AOE archetype.
- Search for `TimedSpawnTag` shows no runtime dependency, or only an explicitly obsolete compatibility declaration.
- Search for `WithNone<CombatLifetimeComponent>` in AOE runtime returns no behavior-critical query.

## Dependencies

Depends on 001 through 005.

## Complexity

Small to medium.


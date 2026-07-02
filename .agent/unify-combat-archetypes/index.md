# Unify Combat Archetypes Plan

## Summary

Refactor projectile and AOE pooling so optional lifetime and timed-spawn behavior is represented by enableable components on stable base archetypes, not by separate archetype variants.

Target result:

- Projectiles: one reusable projectile archetype.
- AOEs: one reusable AOE archetype.
- `CombatLifetimeComponent` stays the lifetime source of truth and is enabled for finite lifetime work, disabled for pulse/one-shot AOE work.
- `TimedSpawnComponent` becomes the timed-spawner source of truth and implements `IEnableableComponent`; it is enabled only when the entity emits interval children.
- `TimedSpawnStateComponent` is always present on reusable combat entities that can host timed spawning; it is reset when timed spawn is enabled.
- `TimedSpawnTag` is removed from hot-path runtime filtering, then removed if no compatibility need remains.

## Architectural Decision

Choose refactor. Do not add a second timed-spawner tag or adapter path.

The current system already uses enableable pooling through `Active`, collision-active tags, render-active tags, tracking, and lifetime. This change extends that model to timed spawning and makes AOE lifetime consistently present. The plan changes existing component lifecycles and queries instead of adding parallel pools.

## Constraints And Invariants

- ECS simulation owns projectile/AOE entities, spawn expansion/apply, pooling, lifetime, timed spawns, and render preparation. Source: `Docs/layers/ecs-simulation.md`.
- Common components and `Active` do not identify domain; systems must require `ProjectileTag` or `AoeTag`. Source: `Docs/coding-standards.md`, `Docs/layers/ecs-simulation.md`.
- Spawn events are intent, commands are one-entity allocation data, expansion owns fan-out math, apply owns reuse and cold creation. Source: `Docs/contracts/spawn-events-and-commands.md`, `Docs/flows/spawn-event-to-entity.md`.
- Hot-path reuse should avoid structural add/remove and despawn by disabling enableable state. Source: `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`, `Docs/reference/simulation/ecs-notes.md`.
- Runtime apply searches disabled `Active` slots and cold-creates overflow. Source: `Docs/flows/spawn-event-to-entity.md`.
- AOE pulse/impact behavior currently depends on disabled or absent lifetime; after this refactor it must depend on disabled `CombatLifetimeComponent`, not absence. Source: `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`, `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`, `Docs/reference/simulation/aoe-system.md`.
- Timed spawn currently depends on `TimedSpawnTag` plus lifetime. After refactor it must depend on enabled `TimedSpawnComponent` plus enabled lifetime/active state. Source: `Assets/Scripts/System/Common/TimedSpawnSystem.cs`.
- ECS lifecycle comments must be updated in same change when component lifecycle changes. Source: `Docs/coding-standards.md`.

## Mechanisms Reused Vs Introduced

Reused:

- Enableable components for hot state (`Active`, collision-active, render-active, tracking, lifetime).
- Existing spawn event/command separation.
- Existing reuse-first plus ECB cold-create fallback.
- Existing `TimedSpawnComponent` and `TimedSpawnStateComponent`.

Introduced:

- `TimedSpawnComponent : IEnableableComponent` as timed-spawn enable bit.
- Unified projectile and AOE archetypes that always include optional timed/lifetime component data.

No new spawn command type, no extra adapter layer, no parallel old/new pool.

## Design Validation

- No hot structural changes: pass. Optional states are enabled/disabled, not added/removed.
- Domain separation: pass. Unified archetypes remain separate by `ProjectileTag` and `AoeTag`.
- Spawn ownership: pass. Expansion still produces commands; apply still materializes commands.
- Reuse correctness: pass if apply resets all optional data and explicitly disables unused enableable states on every reuse.
- Impact AOE correctness: pass if impact queries select disabled lifetime instead of missing lifetime.
- Timed spawn correctness: pass if `TimedSpawnSystem` selects enabled `TimedSpawnComponent`, enabled `CombatLifetimeComponent`, and `Active`.
- Memory tradeoff: acceptable but explicit. Every projectile carries timed-spawn data even when unused; every AOE carries lifetime, pulse VFX, gate buffer, and timed-spawn data. This reduces archetype count at cost of chunk density.
- Parallel safety: easier than current split because fewer concurrent reuse jobs write same component types, but safety attributes should be re-evaluated after implementation.

## Additive Vs Refactor Comparison

Minimal/additive approach:

- Resulting data flow: keep current basic/timed projectile systems and impact/lingering/timed AOE buckets, add enableable timed-spawn fields as extra state.
- New concepts/types introduced: likely a new timed-enable tag or compatibility shim.
- Copies/translations added: extra command routing remains between basic and timed containers.
- Long-term cost: two representations for timed spawn, more stale query logic, archetype count not actually reduced.

Refactor approach:

- Resulting data flow: commands enter one projectile apply path and one AOE apply path; apply resets a single reusable archetype per domain.
- Existing concepts/types changed or removed: `TimedSpawnComponent` becomes enableable; `TimedSpawnTag` is removed from runtime path; AOE impact/lingering no longer means absent/present lifetime.
- Copies/translations removed or avoided: projectile basic vs child-spawner apply split can collapse; AOE lifetime/timed bucket split can collapse.
- Long-term benefit: fewer archetypes, fewer reuse queries, simpler pool behavior, clearer source of truth.

Decision:

Choose refactor. Reason: additive path keeps same archetype split and creates duplicate timed-spawn state, which violates the default decision rule.

## Default Decision Rule

If two representations or data paths describe the same domain concept, refactor toward one source of truth unless there is a concrete compatibility or migration reason not to.

## Task List

- [001 - Component lifecycle model](001-component-lifecycle-model.md)
- [002 - Projectile apply collapse](002-projectile-apply-collapse.md)
- [003 - AOE apply collapse](003-aoe-apply-collapse.md)
- [004 - Query and simulation updates](004-query-and-simulation-updates.md)
- [005 - Tests and assertions](005-tests-and-assertions.md)
- [006 - Docs and cleanup](006-docs-and-cleanup.md)

## Open Questions

- None blocking. Default implementation should remove `TimedSpawnTag` from runtime and tests. If serialization or migration needs it, keep it only as an obsolete compatibility type outside hot-path queries.


# Unify Combat Archetypes Plan

## Summary

Refactor projectile and AOE pooling so optional timed-spawn behavior is
represented by an enableable component on stable base archetypes, collapsing the
timed vs non-timed archetype variants *within each domain*. Keep impact AOE and
lingering AOE as separate archetypes: impact AOE stays lean (no lifetime, no
pulse VFX, no contact-gate buffer, no timed-spawn data) because it is the
high-count hot case, and merging it into lingering would widen every impact
chunk with data it never uses.

Target archetype set: three stable archetypes, down from five.

- `Projectile` — one archetype. Absorbs the current basic and child-spawner
  projectile archetypes via enableable `TimedSpawnComponent`.
- `ImpactAoe` — one archetype. Unchanged from today's impact archetype: no
  `CombatLifetimeComponent`, no `AoePulseVfxComponent`, no
  `AoeContactGateElement`, no timed-spawn data.
- `LingeringAoe` — one archetype. Absorbs the current lingering and
  timed-spawner lingering archetypes via enableable `TimedSpawnComponent`.

Target apply/collision shape: one domain, one apply system, one collision path.

- Projectile apply: one command stream, one disabled-slot query, one scheduled
  reuse job, one cold-create fallback.
- Impact AOE apply: its own apply system with one disabled-slot query, one reuse
  job, one cold-create fallback.
- Lingering AOE apply: its own apply system with one disabled-slot query, one
  reuse job, one cold-create fallback.
- Collision systems stay split by domain: `ImpactAoeCollisionSystem` and
  `LingeringAoeCollisionSystem` remain separate and keep their current queries.

Component lifecycle:

- `CombatLifetimeComponent` stays the lifetime source of truth. It is present on
  projectiles (enabled) and lingering AOEs (enabled on spawn). It is **absent**
  from impact AOEs. Presence therefore remains the impact-vs-lingering
  discriminator for both reuse pools and collision queries.
- `TimedSpawnComponent` becomes the timed-spawner source of truth and implements
  `IEnableableComponent`; it is present on projectiles and lingering AOEs and
  enabled only when the entity emits interval children. It is absent from impact
  AOEs.
- `TimedSpawnStateComponent` is always present on projectiles and lingering AOEs;
  it is reset when timed spawn is enabled.
- `TimedSpawnTag` is removed from hot-path runtime filtering, then removed if no
  compatibility need remains.

## Architectural Decision

Refactor toward one archetype per domain, where the domains are `Projectile`,
`ImpactAoe`, and `LingeringAoe`. Use enableable `TimedSpawnComponent` to collapse
the timed vs non-timed variant pairs inside the projectile and lingering-AOE
domains. Do not merge impact AOE into lingering AOE, and do not add a second
timed-spawner tag or adapter path.

Rationale: the current system already uses enableable pooling through `Active`,
collision-active tags, render-active tags, tracking, and lifetime. Making
`TimedSpawnComponent` enableable extends that model to timed spawning and removes
two archetype variants at negligible chunk-width cost (two small unmanaged
components on entities that already carry lifetime, collision, tracking, render,
and gate data). Merging impact into lingering, by contrast, would force every
impact AOE to carry a `CombatLifetimeComponent`, `AoePulseVfxComponent`, timed
spawn data, and a per-entity `AoeContactGateElement` dynamic buffer it never
uses — a real density and buffer-allocation cost on the highest-count AOE path.

## Constraints And Invariants

- ECS simulation owns projectile/AOE entities, spawn expansion/apply, pooling,
  lifetime, timed spawns, and render preparation. Source: `Docs/layers/ecs-simulation.md`.
- Common components and `Active` do not identify domain; systems must require
  `ProjectileTag` or `AoeTag`, and AOE systems must further discriminate impact
  vs lingering by presence/absence of `CombatLifetimeComponent`. Source:
  `Docs/coding-standards.md`, `Docs/layers/ecs-simulation.md`.
- Spawn events are intent, commands are one-entity allocation data, expansion
  owns fan-out math, apply owns reuse and cold creation. Source:
  `Docs/contracts/spawn-events-and-commands.md`, `Docs/flows/spawn-event-to-entity.md`.
- Hot-path reuse should avoid structural add/remove and despawn by disabling
  enableable state. Source: `Docs/decisions/adr-005-enableable-pooling-for-combat-entities.md`,
  `Docs/reference/simulation/ecs-notes.md`.
- Runtime apply searches disabled `Active` slots and cold-creates overflow.
  Source: `Docs/flows/spawn-event-to-entity.md`.
- Reuse and simulation queries must express slot eligibility in the `EntityQuery`
  itself with `WithAll`, `WithNone`, `WithDisabled`, `WithPresent`, and
  enabled-component query semantics. Do not query a broader archetype/chunk set
  and then branch per entity inside the job to reject wrong slot categories. A
  job may still skip already-active/claimed disabled slots for bounded command
  consumption.
- Each domain has exactly one reusable archetype after this refactor. Spawn apply
  must not keep per-variant bucket systems keyed by timed-spawner. The expected
  shape is direct scheduling against the domain disabled-slot query: projectile
  commands schedule one projectile reuse job; impact commands schedule one impact
  reuse job; lingering commands schedule one lingering reuse job.
- Impact vs lingering pools stay disjoint by `CombatLifetimeComponent` presence.
  Impact reuse selects `WithNone<CombatLifetimeComponent>`; lingering reuse
  selects present lifetime. An impact command must never claim a lingering slot
  or vice versa.
- Timed spawn currently depends on `TimedSpawnTag` plus lifetime. After refactor
  it depends on enabled `TimedSpawnComponent` plus enabled `CombatLifetimeComponent`
  and `Active`. Source: `Assets/Scripts/System/Common/TimedSpawnSystem.cs`.
- Invariant: enabling `TimedSpawnComponent` requires enabling
  `CombatLifetimeComponent`. `TimedSpawnSystem` requires enabled lifetime, so a
  timed spawner on a disabled/absent-lifetime entity is silently dead. Timed
  spawn is therefore only valid on projectiles and lingering AOEs, never impact.
- ECS lifecycle comments must be updated in the same change when component
  lifecycle changes. Source: `Docs/coding-standards.md`.

## Mechanisms Reused Vs Introduced

Reused:

- Enableable components for hot state (`Active`, collision-active, render-active,
  tracking, lifetime).
- Existing spawn event/command separation.
- Existing reuse-first plus ECB cold-create fallback.
- Existing `TimedSpawnComponent` and `TimedSpawnStateComponent` data.
- Existing impact and lingering collision systems and their queries
  (`WithNone` / `WithPresent<CombatLifetimeComponent>`), unchanged.
- Existing `CombatLifetimeSystem`, `AoePulseVfxSystem`, and `AoeContactGateSystem`
  which already only touch lingering AOEs.

Introduced:

- `TimedSpawnComponent : IEnableableComponent` as the timed-spawn enable bit.
- Unified `Projectile` archetype (basic + child-spawner) and unified
  `LingeringAoe` archetype (lingering + timed-spawner lingering).
- Split of the single `AoeSpawnApplySystem` into `ImpactAoeSpawnApplySystem` and
  `LingeringAoeSpawnApplySystem`.
- AOE expansion routing commands into two containers (impact vs lingering),
  mirroring projectile expansion.

No new spawn command type, no extra adapter layer, no parallel old/new pool.

## Design Validation

- No hot structural changes: pass. Timed-spawn state is enabled/disabled, not
  added/removed.
- Domain separation: pass. Three archetypes remain separated by `ProjectileTag`,
  `AoeTag`, and `CombatLifetimeComponent` presence.
- Impact leanness: pass. Impact archetype is unchanged; it gains no lifetime,
  pulse VFX, timed-spawn, or contact-gate buffer.
- Pool disjointness: pass. Impact reuse (`WithNone<CombatLifetimeComponent>`) and
  lingering reuse (present lifetime) cannot claim each other's slots.
- Collision correctness: pass with no change. Impact keeps `WithNone` lifetime;
  lingering keeps `WithPresent` lifetime. No entity matches both.
- Spawn ownership: pass. Expansion produces commands; apply materializes them.
- Reuse correctness: pass if each apply path resets all optional data and
  explicitly sets timed-spawn enabled state on every reuse. A lingering reuse
  must set/enable timed spawn from the command or disable it; a projectile reuse
  must set/enable timed spawn from `cmd.HasTimedSpawner` or disable it.
- Apply simplification: pass if projectile, impact, and lingering apply each have
  one disabled-slot query and one scheduled reuse job. The `AoeSpawnKey`
  bucket/query maps and the degenerate projectile bucket sort are removed.
- Query correctness: pass if systems schedule only the entities they intend to
  process. Active-skip for bounded command consumption is allowed; per-entity
  `if` checks must not be the primary filter for timed-spawn or domain category.
- Timed spawn correctness: pass if `TimedSpawnSystem` selects enabled
  `TimedSpawnComponent`, enabled `CombatLifetimeComponent`, and `Active`.
- Memory tradeoff: acceptable and bounded. Projectiles and lingering AOEs each
  carry timed-spawn data (two small components) even when unused. Impact AOEs are
  unaffected. This removes two archetypes at minimal chunk-width cost and keeps
  the impact hot path lean.
- Parallel safety: unchanged for collision/lifetime/pulse. Apply reuse jobs write
  disjoint archetypes; safety attributes should be re-evaluated after implementation.

## Additive Vs Refactor Comparison

Minimal/additive approach:

- Resulting data flow: keep separate basic/timed projectile archetypes and
  separate lingering/timed-lingering AOE archetypes, add enableable timed-spawn
  fields as extra unused state.
- New concepts/types introduced: likely a new timed-enable tag or compatibility shim.
- Copies/translations added: extra command routing remains between basic and
  timed containers, and between lingering and timed-lingering buckets.
- Long-term cost: two representations for timed spawn, more stale query logic,
  archetype count not actually reduced.

Refactor approach:

- Resulting data flow: projectile commands enter one projectile apply path; AOE
  commands route to impact apply or lingering apply; each apply resets a single
  reusable archetype per domain and sets timed-spawn enabled state per command.
- Existing concepts/types changed or removed: `TimedSpawnComponent` becomes
  enableable; `TimedSpawnTag` is removed from the runtime path; timed-lingering
  stops being a distinct archetype; basic/child-spawner projectile stop being
  distinct archetypes; `AoeSpawnApplySystem` splits into impact and lingering
  apply systems.
- Copies/translations removed or avoided: projectile basic vs child-spawner apply
  split is removed; the timed-vs-non-timed bucket split inside AOE apply is removed.
- Long-term benefit: three archetypes instead of five, one reuse query/job per
  domain, simpler pool behavior, clearer source of truth, impact hot path stays lean.

Decision:

Refactor. Reason: the additive path keeps duplicate timed-spawn representations
and does not reduce archetype count, violating the default decision rule. Keeping
impact AOE separate is not a violation of that rule — impact and lingering are
genuinely different domain concepts with different data and different collision
behavior, and merging them trades a real per-entity buffer/density cost for no
conceptual unification.

## Default Decision Rule

If two representations or data paths describe the same domain concept, refactor
toward one source of truth unless there is a concrete compatibility, migration,
or performance reason not to. Timed vs non-timed within a domain is the same
concept (refactor). Impact vs lingering is not (keep separate; merging carries a
concrete performance cost).

## Task List

- [001 - Component lifecycle model](001-component-lifecycle-model.md)
- [002 - Projectile apply collapse](002-projectile-apply-collapse.md)
- [003 - AOE apply split and lingering collapse](003-aoe-apply-collapse.md)
- [004 - Query and simulation updates](004-query-and-simulation-updates.md)
- [005 - Tests and assertions](005-tests-and-assertions.md)
- [006 - Docs and cleanup](006-docs-and-cleanup.md)

## Open Questions

- None blocking. Default implementation removes `TimedSpawnTag` from runtime and
  tests. If serialization or migration needs it, keep it only as an obsolete
  compatibility type outside hot-path queries.

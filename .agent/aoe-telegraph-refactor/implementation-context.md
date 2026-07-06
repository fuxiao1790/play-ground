# Implementation Context

## Architectural Decisions
- Use positive `LingeringAoeTag` as the impact-vs-lingering AOE discriminator.
- Keep `CombatLifetimeComponent` on projectiles and lingering AOEs only; impact AOEs stay without lifetime.
- Remove lifetime enable-bit meaning; lifetime becomes plain timer data after dead reads are gone.
- Windup is out of scope and will use separate windup data later.

## Global Invariants
- Preserve behavior: impact AOEs hit once and deactivate; lingering AOEs repeat by hit gate and expire by lifetime.
- `Active` is the reusable-slot occupancy gate.
- Reuse never crosses impact and lingering archetypes.
- Projectiles still use `CombatLifetimeComponent.Remaining`.

## Ownership Boundaries
- AOE discriminator types and AOE-only data live under `Assets/Scripts/System/Aoe`.
- Domain-neutral combat data lives under `Assets/Scripts/System/Common`.
- Tests may update only assertions/setup directly affected by component shape.

## Data Flow
- Spawn expansion writes AOE/projectile commands.
- Spawn apply claims inactive slots or cold-creates entities, then resets all runtime fields.
- Collision systems consume active domain entities and emit hit/spawn/VFX queues.
- `CombatLifetimeSystem` counts lifetime down and disables `Active` on expiry.

## Lifecycle / Allocation Rules
- Combat entities are pooled by disabling `Active`, not by destroying on hot path.
- Lingering archetype includes `LingeringAoeTag`; impact archetype excludes it.
- No new runtime data path or system is introduced by this refactor.

## ECS / Job / Threading Constraints
- Keep jobs Burst-compatible and allocation-light.
- Avoid structural changes inside hot jobs.
- Preserve enableable pooling for `Active`, collision/render tags, and timed spawn.

## Determinism Requirements
- Do not change tick cadence, hit order, lifetime countdown, spawn jitter, or reuse semantics.

## Producer / Consumer Separation
- Do not mix combat hit events with spawn or VFX payloads.
- Keep queue ownership through singleton lanes.

## Reused Mechanisms
- Zero-size ECS tags.
- Presence-based query filtering, now on named discriminator tag.
- Existing reusable archetypes and `Active` occupancy.

## Introduced Mechanisms
- `LingeringAoeTag`, a zero-size `IComponentData`.

## Validation Requirements
- Build compiles.
- Search checks prove no stale lifetime routing/enable-bit APIs remain after relevant tasks.
- Relevant AOE/projectile playmode suites should pass or inability to run must be reported.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Aoe/AoeEcsComponents.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs`
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Docs/reference/simulation/ecs-notes.md`

# Implementation Context

## Architectural Decisions
- Target exactly three combat archetypes: projectile, impact AOE, lingering AOE.
- Timed spawning is an enableable `TimedSpawnComponent` on projectile and lingering AOE entities.
- Impact AOE stays lean and does not gain lifetime, pulse VFX, contact gates, or timed-spawn data.

## Global Invariants
- `Active` and common components do not identify a domain; systems must require `ProjectileTag` or `AoeTag`.
- Impact vs lingering AOE is discriminated by `CombatLifetimeComponent` absence/presence.
- Enabling `TimedSpawnComponent` requires enabled `CombatLifetimeComponent`.
- `TimedSpawnComponent` and `TimedSpawnStateComponent` are absent from impact AOEs.

## Ownership Boundaries
- ECS simulation owns projectile/AOE entities, spawn expansion/apply, pooling, lifetime, timed spawns, and render prep.
- Expansion owns event fan-out and command production.
- Apply owns disabled-slot reuse and cold entity creation.

## Data Flow
- `ProjectileSpawnEvent` / `AoeSpawnEvent` carry spawn intent.
- `ProjectileSpawnCommand` / `AoeSpawnCommand` carry one-entity allocation data.
- Projectile expansion writes one command container.
- AOE expansion writes impact commands and lingering commands separately.

## Lifecycle / Allocation Rules
- Reuse disabled slots before cold creation.
- Hot state changes use enableable components rather than structural add/remove.
- Reuse queries must express domain/category selection with query construction, not per-entity eligibility branches.
- ECS lifecycle comments must change with lifecycle changes.

## ECS / Job / Threading Constraints
- Reuse jobs operate on disabled `Active` slots selected by query.
- Jobs may skip already-active slots for bounded command consumption.
- No hot-path `TimedSpawnTag` simulation filter remains.

## Determinism Requirements
- Timed spawn state reset preserves deterministic jitter based on source id, seed, and tick.

## Producer / Consumer Separation
- Spawn expansion does not allocate entities.
- Apply systems do not reinterpret spawn events.
- Damage, spawn follow-up, and VFX event paths remain separate.

## Reused Mechanisms
- Enableable `Active`, collision/render active tags, tracking, and lifetime.
- Existing command structs and timed-spawn data.
- Existing impact and lingering collision query split.

## Introduced Mechanisms
- `TimedSpawnComponent : IEnableableComponent`.
- Single `ProjectileSpawnApplySystem`.
- `ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem`.

## Validation Requirements
- Project compiles.
- Tests no longer rely on `TimedSpawnTag`.
- Search confirms retired system/type names and tag runtime dependencies are removed.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Common/CombatEcsComponents.cs`
- `Assets/Scripts/System/Common/TimedSpawnSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`
- `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`

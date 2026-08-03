# 005 — Spawn Apply Lane Split

**Depends on:** 001, 004
**Scope:** medium (one refactor + one new system; contains the plan's highest-risk edit)

## Goal

Give the swept archetype its own pool and apply system, and stop the discrete lane from
consuming swept slots.

## The two archetypes

Sweep and tracking being exclusive makes these genuinely different component sets, not a
tag flavour of one shape:

| | Discrete | Swept |
|---|---|---|
| `ProjectileTag` | yes | yes |
| `SweptProjectileTag` | — | **yes** |
| `ProjectileSweepComponent` | — | **yes** |
| `ProjectileTrackingComponent` | yes | **no** |
| everything else | identical | identical |

The swept archetype is *smaller*: it drops `ProjectileTrackingComponent` (bool + 4 floats +
2 ints + `float2` + `uint`) and gains only a `float2` and a zero-size tag.

## Tracking excludes itself — no change needed

`ProjectileTargetAcquisitionJob` takes `ref ProjectileTrackingComponent`
([ProjectileTrackingSystem.cs:71](../../Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs#L71))
and `ProjectileSteeringJob` takes `in ProjectileTrackingComponent`
([:389](../../Assets/Scripts/System/Projectiles/ProjectileTrackingSystem.cs#L389)). `IJobEntity`
only matches archetypes that have every `Execute` parameter's component, so both jobs skip
the swept archetype automatically. **Do not** add a `WithNone<SweptProjectileTag>` to
tracking — it would be redundant and imply the exclusion is a filter rather than a
structural fact.

## The highest-risk edit

`ProjectileTag` is on **both** archetypes, so `ProjectileSpawnApplySystem._deadSlotQuery`
([:69-72](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L69-L72))
currently matches every disabled projectile slot. It must become:

```csharp
_deadSlotQuery = new EntityQueryBuilder(Allocator.Temp)
    .WithAll<ProjectileTag>()
    .WithNone<SweptProjectileTag>()
    .WithDisabled<Active>()
    .Build(this);
```

Exactly what `ImpactAoeSpawnApplySystem` does with `.WithNone<LingeringAoeTag>()`
([AoeSpawnApplySystem.cs:65](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L65)).

Without it the discrete job would fetch a `ComponentTypeHandle<ProjectileTrackingComponent>`
against swept chunks that have no such component. That does not fail cleanly — it is a
silent-corruption path, not a crash. Task 008 covers it mechanically.

## Changes

### Extract shared materialization first

Pull the per-slot write body out of `ProjectileSpawnJob.Execute`
([:294-336](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L294-L336))
into an internal static `ProjectileSpawnApplyUtility.WriteCommon(...)`, mirroring
`AoeSpawnApplyUtility.WriteCommon` and its stated purpose at
[AoeSpawnApplySystem.cs:555-558](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L555-L558).

**`tracking[i] = cfg.Tracking` must stay out of `WriteCommon`** — the swept lane has no
tracking array to write. It belongs in the discrete job only. Same for the
`trackingMask[i]` enable write.

Move `SpawnState`, `SpawnStateFor`, `NeedsCollision`, `ArmingFor`, `IsArming`,
`HitPayloadFor`, and `InitialTimedSpawnStateFor` onto the utility unchanged.

Land this as a **pure refactor with no behavior change**, existing tests green, before
writing any swept code.

### New system: `SweptProjectileSpawnApplySystem`

`Assets/Scripts/System/Projectiles/SweptProjectileSpawnApplySystem.cs`. Copy of
`ProjectileSpawnApplySystem` with five differences:

1. **Archetype** — the list at
   [:49-67](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L49-L67)
   **minus** `typeof(ProjectileTrackingComponent)`, **plus** `typeof(SweptProjectileTag)`
   and `typeof(ProjectileSweepComponent)`.
2. **Dead-slot query** — `.WithAll<ProjectileTag>().WithAll<SweptProjectileTag>().WithDisabled<Active>()`.
3. **Command source** — `SweptCommands` instead of `Commands`; same `PendingHandle`.
4. **No tracking handle** — the job must not declare `ComponentTypeHandle<ProjectileTrackingComponent>`.
5. **Seed the origin** — after `WriteCommon`:
   `sweeps[i] = new ProjectileSweepComponent { Origin = cfg.Position };`

   Redundant on the normal path once `SweptProjectileOriginSystem` (task 006) runs every
   frame before movement, but it covers the arming window: arming projectiles are excluded
   from both capture and collision, so `Origin` would otherwise hold a previous pool
   occupant's value until the tag clears. Cheap, and it means no path can read an
   uninitialized origin.

Give it its own `ProfilerMarker`s / `ProfilerCounterValue`s under a
`SweptProjectileSpawnApplySystem.*` prefix so the lanes are separable in the profiler, and
accumulate into `CombatStatsSingleton.EntitiesSpawnedViaReuse` / `EntitiesSpawnedViaEcb`
exactly as the discrete lane does — the pool-cleanup calm-down gate derives despawns from
those counters and would skew if one lane went uncounted.

## Systems verified to need NO change

Recorded so a reviewer need not re-derive it:

| System | Query | Why it works |
|---|---|---|
| `ProjectileTrackingSystem` | `ref`/`in ProjectileTrackingComponent` in `Execute` | swept archetype lacks the component; self-excluding |
| `ProjectileContactGateSystem` | `WithAll(ProjectileTag, Active)` | both archetypes carry both |
| `CombatLifetimeSystem` | `WithAll(ProjectileTag, Active, CombatLifetimeComponent)` | same |
| `CombatRenderPrepareSystem` | common render + kinematics + `Active`, no domain tag | swept carries all of them |
| `CombatStatsGatherSystem` | `WithAll(CombatRenderComponent, Active, ProjectileTag)` | counts both lanes |
| `CombatPoolCleanupSystem` | `WithAny<ProjectileTag, AoeTag>`, per-chunk | explicitly pool-agnostic ([:77-78](../../Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs#L77-L78)) |
| `SpawnPoolTopUp` | takes archetype + query as parameters | already lane-neutral |

## Acceptance Criteria

- Discrete `_deadSlotQuery` excludes `SweptProjectileTag`.
- Swept archetype omits `ProjectileTrackingComponent` entirely.
- Tracking write stays out of the shared `WriteCommon`.
- Materialization refactor lands separately, behavior-neutral, tests green.
- `ProjectileSweepComponent.Origin` seeded from `cfg.Position` on every reuse.
- Both lanes report spawn counts into `CombatStatsSingleton`.
- No `WithNone<SweptProjectileTag>` added to the tracking systems.

## Risks

- **Forgetting `WithNone`** — highest-consequence, lowest-visibility failure in the plan.
- **Cross-lane reuse is impossible by construction.** A swept slot can never serve a
  discrete command or vice versa, so a bursty lane cold-creates rather than borrowing.
  Accepted cost of the split; matches AOE behavior.

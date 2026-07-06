# 001 — `AoeWindupComponent` + `AoeWindupSystem` + consumer filters

## Goal
Add the windup phase component and the system that ticks it and activates the AOE on expiry, plus
the two "skip during windup" consumer filters. Inert until materialization (003) actually enables the
component, so this task is behavior-identical on its own.

## Changes

1. **New component** in [AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs):
   ```csharp
   // ECS Lifecycle: enableable AOE windup phase; added at entity creation on both AOE archetypes;
   // enabled == pre-impact windup (collision/timed suppressed, lifetime frozen, telegraph showing);
   // disabled (default) == activated/normal. Reset on reuse. Ticked by AoeWindupSystem.
   public struct AoeWindupComponent : IComponentData, IEnableableComponent
   {
       public float Remaining;
       public bool ActivateCollision;   // NeedsCollision(cmd) captured at spawn
       public bool ActivateTimedSpawn;  // HasTimedSpawner(cmd) captured at spawn (lingering only)
   }
   ```

2. **New system** `AoeWindupSystem` (`Assets/Scripts/System/Aoe/AoeWindupSystem.cs`):
   - `[UpdateInGroup(typeof(SimulationSystemGroup))]`, `[UpdateBefore(typeof(CombatLifetimeSystem))]`
     (that system is already before both collision systems and `TimedSpawnSystem`).
   - Two `[BurstCompile]` `IJobEntity` (parallel), both selecting **enabled** `AoeWindupComponent`:
     - **ImpactWindupJob** — `[WithAll(typeof(AoeTag), typeof(Active))]`,
       `[WithNone(typeof(LingeringAoeTag))]`. Params:
       `ref AoeWindupComponent windup`, `EnabledRefRW<AoeCollisionActiveTag> collision`,
       `EnabledRefRW<AoeWindupComponent> windupEnabled`.
     - **LingeringWindupJob** — `[WithAll(typeof(AoeTag), typeof(Active), typeof(LingeringAoeTag))]`.
       Params: as above **plus** `EnabledRefRW<TimedSpawnComponent> timedSpawn`.
   - Body (shared logic):
     ```csharp
     windup.Remaining -= DeltaTime;
     if (windup.Remaining > 0f) return;
     if (windup.ActivateCollision) collision.ValueRW = true;
     // lingering only:
     if (windup.ActivateTimedSpawn) timedSpawn.ValueRW = true;
     windupEnabled.ValueRW = false; // leave windup phase
     ```
   - No VFX writer needed here (telegraph is emitted at spawn in expansion, task 004).
   - `state.Dependency` threads both jobs (lingering after impact only if they touch shared data —
     they don't; schedule independently and combine).

3. **Consumer filters** (freeze lifetime + suppress pulse during windup):
   - [CombatLifetimeSystem.cs:97](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L97)
     `AoeLifetimeJob`: add `[WithDisabled(typeof(AoeWindupComponent))]`. (AOE-only job; both archetypes
     carry the component, so disabled == not in windup.)
   - [AoePulseVfxSystem.cs:42](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs#L42)
     `AoePulseVfxJob`: add `[WithDisabled(typeof(AoeWindupComponent))]`.
   - **Do NOT** filter collision systems or `TimedSpawnSystem` — they already gate on
     `AoeCollisionActiveTag`/`TimedSpawnComponent`, which are disabled during windup; `TimedSpawnSystem`
     is shared with projectiles (which lack `AoeWindupComponent`) and must not be filtered.

## Acceptance criteria
- Compiles. With nothing enabling `AoeWindupComponent` yet, all existing AOE/projectile sim tests pass
  unchanged (the component is present-but-disabled once 003 adds it; here it does not exist on
  archetypes yet, so the filters match everything as before — verify `WithDisabled` on an
  absent-for-now component does not empty the queries, i.e. land this together with 003 if the test
  harness archetypes lack the component).

## Note on landing order
`WithDisabled<AoeWindupComponent>` requires the component present on the queried entities. If 001 is
committed before 003 adds the component to the archetypes, `AoeLifetimeJob`/`AoePulseVfxJob` would
match nothing. **Land 001+003 together (or add the component to archetypes first).** Keep the system
+ component in 001 for review clarity, but treat 001+003 as one green checkpoint.

## Scope / complexity
Medium. One component, one system with two jobs, two attribute additions.

## Dependencies
Refactor plan complete (`LingeringAoeTag`, plain lifetime). Green-checkpoints with 003.

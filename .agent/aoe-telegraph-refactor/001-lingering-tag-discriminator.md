# 001 — Introduce `LingeringAoeTag`; migrate routing off lifetime presence

## Goal
Replace the impact-vs-lingering discriminator (`CombatLifetimeComponent` present = lingering,
absent = impact) with an explicit zero-size `LingeringAoeTag`. Behavior-identical.

## Changes

1. **New component** in [AoeEcsComponents.cs](../../Assets/Scripts/System/Aoe/AoeEcsComponents.cs):
   ```csharp
   // ECS Lifecycle: lingering-AOE discriminator tag; added at entity creation; kept until
   // root teardown; present only on lingering AOEs (duration-based, repeat-hit). Absence marks
   // an impact AOE (single contact pass). Replaces CombatLifetimeComponent presence as the
   // impact-vs-lingering routing discriminator.
   public struct LingeringAoeTag : IComponentData { }
   ```

2. **Add the tag to the lingering archetype** in
   [AoeSpawnApplySystem.cs:242](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L242)
   (`_lingeringArchetype = CreateArchetype(... typeof(LingeringAoeTag) ...)`). Do **not** add
   it to `_impactArchetype`. No write needed at materialization/reuse — the tag is structural
   (always present on the lingering archetype, always absent on impact); reuse never crosses
   archetypes.

3. **Migrate every presence-based query** (all in lockstep):
   - [ImpactAoeCollisionSystem.cs:31](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs#L31)
     query `.WithNone<CombatLifetimeComponent>()` → `.WithNone<LingeringAoeTag>()`.
   - [ImpactAoeCollisionSystem.cs:125](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs#L125)
     job attribute `[WithNone(typeof(CombatLifetimeComponent))]` → `[WithNone(typeof(LingeringAoeTag))]`.
   - [LingeringAoeCollisionSystem.cs:30](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs#L30)
     query `.WithPresent<CombatLifetimeComponent>()` → `.WithAll<LingeringAoeTag>()`.
     Leave the job's `[WithPresent(typeof(CombatLifetimeComponent))]` attribute and the
     `EnabledRefRO<CombatLifetimeComponent>` param for now — they are removed in 002/003.
     (The `EnabledRefRO` param already forces the component present, so the tag + param together
     still select exactly the lingering set.)
   - [AoeSpawnApplySystem.cs:51](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L51)
     impact dead-slot query `.WithNone<CombatLifetimeComponent>()` → `.WithNone<LingeringAoeTag>()`.
   - [AoeSpawnApplySystem.cs:263](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L263)
     lingering dead-slot query `.WithAll<CombatLifetimeComponent>()` → `.WithAll<LingeringAoeTag>()`.

4. **Tests** — migrate presence assertions/queries to the tag:
   - [AoeSimulationTests.cs:510-513](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L510):
     `HasComponent<CombatLifetimeComponent>(impact) == false` → `HasComponent<LingeringAoeTag>(impact) == false`;
     `...(lingering) == true` → `HasComponent<LingeringAoeTag>(lingering) == true`.
   - [AoeSimulationTests.cs:1439](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1439),
     [:1448](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1448),
     [:1507](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1507),
     [:1530](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1530),
     [:1541](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1541) — any helper query using
     `WithNone/WithAll<CombatLifetimeComponent>` to select impact/lingering → the tag.
   - [CombatPoolCleanupSystemTests.cs:395](../../Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs#L395)
     `ImpactAoeQuery` `.WithNone<CombatLifetimeComponent>()` → `.WithNone<LingeringAoeTag>()`.
   - Leave `IsComponentEnabled<CombatLifetimeComponent>` asserts ([:516](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L516)/[:583](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L583)/[:592](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L592))
     alone here; they are handled in 003.

## Do NOT touch in this task
- `TimedSpawnSystem` query (`WithAll<CombatLifetimeComponent>` there is presence-based across
  projectiles + lingering and is already impact-excluded by `TimedSpawnComponent`).
- `CombatLifetimeSystem` `AoeLifetimeJob` `WithAll<CombatLifetimeComponent>` (still correct as a
  presence/timer selector, lingering-only).
- Any enable-bit logic — that is 002/003.

## Acceptance criteria
- Project compiles.
- Grep confirms no remaining impact/lingering **routing** query keys on
  `CombatLifetimeComponent` presence (only `CombatLifetimeSystem`/`TimedSpawnSystem` timer
  presence uses remain).
- All AOE simulation tests pass unchanged in meaning (impact one-pass, lingering repeat + expire,
  pool reuse impact↔impact and lingering↔lingering).

## Scope / complexity
Low. Mechanical, one new tag, ~10 query sites + test asserts. No job-body logic changes.

## Dependencies
None. Precedes 002/003.

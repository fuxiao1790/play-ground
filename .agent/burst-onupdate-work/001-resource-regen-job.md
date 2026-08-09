# 001 — `ResourceRegenSystem` → Burst `IJobEntity`

## Why

`ResourceRegenSystem` is the only system in the codebase with **no job at all**.
The struct carries no `[BurstCompile]`, and `OnUpdate` is two managed
`SystemAPI.Query` foreach passes:

`Assets/Scripts/System/Targets/ResourceRegenSystem.cs:10-28`

```csharp
public void OnUpdate(ref SystemState state)
{
    float deltaTime = SystemAPI.Time.DeltaTime;
    foreach (RefRW<Health> health in SystemAPI.Query<RefRW<Health>>()) { ... }
    foreach (RefRW<Mana> mana in SystemAPI.Query<RefRW<Mana>>()) { ... }
}
```

It touches no native container, no managed object, and makes no structural
change — there is nothing holding it on the main thread and nothing to complete.
It is the smallest possible instance of the pattern every other task in this
plan follows, which is why it is recommended to land first even though its
absolute cost is low (`Docs/performance.md` caps targets at roughly 50).

## Scope

- `Assets/Scripts/System/Targets/ResourceRegenSystem.cs`

## Change

Replace both foreach bodies with two `[BurstCompile]` `IJobEntity` structs,
scheduled in sequence off `state.Dependency`.

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayGround.System.Combat.Application.CombatApplyFinalizeSingleSystem))]
public partial struct ResourceRegenSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;

        JobHandle healthHandle = new HealthRegenJob
        {
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);

        state.Dependency = new ManaRegenJob
        {
            DeltaTime = deltaTime
        }.ScheduleParallel(healthHandle);
    }

    [BurstCompile]
    private partial struct HealthRegenJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(ref Health health)
        {
            health.Current = math.min(
                health.Max,
                health.Current + health.RegenPerSecond * DeltaTime);
        }
    }

    [BurstCompile]
    private partial struct ManaRegenJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(ref Mana mana)
        {
            mana.Current = math.clamp(
                mana.Current + mana.RegenPerSecond * DeltaTime,
                0f,
                mana.Max);
        }
    }
}
```

### Do not merge the two passes into one job

This is the one real trap in an otherwise mechanical task. A single
`Execute(ref Health, ref Mana)` would require **both** components and silently
stop regenerating entities that carry only one. That is not hypothetical — the
existing tests create exactly such entities:

- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs:176-177`, `:195`
  — `CreateEntity(typeof(Mana))`, no `Health`
- `Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs:78`, `:105` —
  `CreateEntity(typeof(Mana))`, no `Health`
- `Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs:83` —
  `CreateEntity(typeof(Health))`, no `Mana`

Two jobs preserve today's semantics exactly. The two-pass structure is load
bearing, not incidental.

### Scheduling choice

`ScheduleParallel` rather than `.Run()`: this system has no native-container
access and no producer handles to reconcile, so `state.Dependency` chaining is
the whole dependency story and there is nothing tricky to manage. Chaining
`ManaRegenJob` off `healthHandle` rather than scheduling both off
`state.Dependency` costs nothing — they touch disjoint components, but the
sequential chain keeps `state.Dependency` a single handle without a
`CombineDependencies` call.

`using Unity.Burst;` and `using Unity.Jobs;` need adding; `Unity.Entities` and
`Unity.Mathematics` are already imported.

## Acceptance Criteria

- `ResourceRegenSystem.OnUpdate` contains no `foreach` and no per-entity work —
  only job construction and scheduling.
- Two separate job structs exist; neither requires both `Health` and `Mana`.
- Both job structs carry `[BurstCompile]`.
- `state.Dependency` is assigned the final handle.
- Regen math is byte-for-byte the same: `math.min` for health (no lower clamp),
  `math.clamp(…, 0f, Max)` for mana. The asymmetry is preserved deliberately —
  ECS is allowed to push health negative and actor roots clamp for display
  (`Docs/reference/simulation/project-ecs-implementation.md` §*Collision Event
  Dispatch*), so adding a lower clamp to health here would be a behavior change.
- System group and `[UpdateAfter]` attributes unchanged.

## Dependencies

None. Recommended first landing — it establishes the extract-to-job-struct shape
the rest of the plan repeats.

## Scope/Complexity

Small. One file, ~40 lines, no cross-system coupling.

## Test Coverage

`Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs` and
`Assets/Tests/EditMode/TargetedRoutingEditModeTests.cs` exercise the single-
component entities this task must not break. No new tests required.

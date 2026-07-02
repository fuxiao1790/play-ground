# Implementation Context

## Architectural Decisions
- Replace worker-lane spawn expansion/apply with ordered command lists and one single-threaded Burst reuse job per apply pool.
- Data flow is `SpawnEvent` -> expansion `IJob` -> `NativeList<SpawnCommand>` -> reuse `IJob` -> cold-create unreused suffix.
- Do not reintroduce worker-owned command-index streams, worker ranges, or remainder queues.

## Global Invariants
- ECS simulation owns projectile/AOE entities, spawn expansion/apply, and `Active`-based reuse.
- Domain identity comes from `ProjectileTag` or `AoeTag`; `Active` and scope membership are not domain markers.
- Events are gameplay intent. Commands are one-entity allocation intent.

## Ownership Boundaries
- Expansion systems drain event queues and scope buffers, read template registries, run spawn math, and produce commands.
- Apply systems own disabled-slot queries, reuse, and cold creation.
- Managed gameplay, collision, status, and timed-spawn producers do not directly create projectile/AOE entities.

## Data Flow
- `ProjectileSpawnExpansionSystem` produces one ordered `NativeList<ProjectileSpawnCommand>`.
- `AoeSpawnExpansionSystem` produces ordered `NativeList<AoeSpawnCommand>` containers for impact and lingering AOEs.
- Apply systems consume commands by cursor order and cold-create only commands after the reused prefix.

## Lifecycle / Allocation Rules
- Hot despawn uses enableable state, especially `Active`; it does not destroy/create during normal churn.
- Projectile slots carry timed-spawn components and enable/disable timed behavior.
- Impact AOE slots stay lean and lifetime/timed-spawn absent. Lingering AOE slots carry lifetime and timed-spawn state.

## ECS / Job / Threading Constraints
- Expansion and reuse jobs are Burst `IJob`s.
- Native producers must complete before expansion drains queues.
- Apply runs after expansion and after current-frame collision; spawned/reused entities join simulation on the next update.

## Determinism Requirements
- Command order is the single source of truth for apply order.
- Reuse walks disabled chunks in query order with one command cursor.

## Producer / Consumer Separation
- Spawn math stays in expansion.
- Entity reset, enableable state, reuse, and cold creation stay in apply.

## Reused Mechanisms
- Existing event -> command -> apply contracts.
- Existing projectile, impact AOE, and lingering AOE pool queries.
- Existing ECB cold-create fallback.

## Introduced Mechanisms
- Ordered `NativeList<TCommand>` containers owned by expansion systems.
- Reuse jobs report a reused prefix length through `NativeReference<int>`.

## Validation Requirements
- Build at least `PlayGround.Runtime.csproj`.
- Prefer focused Unity spawn pipeline tests when Unity can run.
- Search confirms no `ParallelDeadSlotSpawnApply`, command-index streams, or remainder queues remain in runtime spawn apply paths.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs`
- `Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Common/ParallelDeadSlotSpawnApply.cs`
- `Docs/flows/spawn-event-to-entity.md`
- `Docs/profiling.md`
- `Docs/reference/simulation/project-ecs-implementation.md`
- `Docs/reference/simulation/projectile-system.md`
- `Docs/reference/simulation/aoe-system.md`
- `Docs/reference/simulation/project-aoe-system-common.md`

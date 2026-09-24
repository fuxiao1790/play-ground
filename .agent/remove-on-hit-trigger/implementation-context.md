# Implementation Context

## Architectural Decisions
- Full removal of the on-hit-trigger mechanism: authoring type, compiler wiring, six
  `Runtime*Definition` fields, `OnHitSpawnRef`, every ECS field/branch that exists only to carry
  or fire it, docs, tests.
- `ProjectileHitPayload` wrapper is kept (only its `OnHitSpawn` field/param drops) — collapsing it
  into `CombatHitPayload` is explicitly out of scope (see index.md "Explicitly deferred").
- `RuntimeTargetedDefinition.OnHitAoeSpawnDefinition`/`OnHitProjectileSpawnDefinition` (never wired
  by the compiler) are deleted as dead placeholders, not preserved.

## Global Invariants
- No live `SkillLoadout` asset references `OnHitTrigger` — every code change here is
  behavior-neutral for current content. Only `TriggerCatalog.asset` lists it (editor step, task 009).
- `ProjectileSpawnEvent`/`ImpactAoeSpawnEvent`/`LingeringAoeSpawnEvent`/`TargetedSpawnEvent` queues,
  their `*SpawnExpansionSystem`s, and `*SpawnApplySystem`s stay untouched except for the specific
  on-hit-only lines called out per task — `TimedSpawnSystem.cs` and `StatusProcessSystem.cs` keep
  writing to them unchanged.
- Burst job structs and the archetypes they query must be edited together within a task — never
  leave a struct referencing a field that its archetype no longer creates.

## Ownership Boundaries
- `SkillSetCompiler`/`SkillLoadoutCompiler`/`SkillDriver` own compile-time wiring (Skills layer).
- `IntervalChildTemplates.cs`/`SpawnTemplateComponents.cs` own the shared spawn-template/refcount
  types used by all three domains (Projectile/Aoe/Targeted).
- `AoeCollisionCore`/`ProjectileHitEmission` own per-domain collision-to-spawn-event dispatch.
- `CombatRoot.cs` owns command construction + registry-template normalization for all 3 domains.

## Data Flow
- Compile (Skills) → SkillDriver registration (sounds/types/templates) → managed spawn request →
  `CombatRoot` command → ECS spawn-apply → collision job → hit event / (removed: on-hit spawn
  event) → apply/despawn refcounting via `SpawnTemplateRefEmit`.

## Lifecycle / Allocation Rules
- Archetypes are fixed at `OnCreate` for each spawn-apply system; component list changes require
  updating the `EntityManager.CreateArchetype(...)` call in the same task as the component deletion.

## ECS / Job / Threading Constraints
- `[BurstCompile]` job structs must not carry unused `ComponentTypeHandle`/`NativeQueue.ParallelWriter`
  fields — dead handles left in a job struct after this removal must be deleted, not left unused.
- Ref counting (`SpawnTemplateRefEmit`) must run in matching Acquire/Release pairs; do not change
  one without the other.

## Determinism Requirements
- None specific to this removal (no RNG/seeding involved in the fields being deleted).

## Producer / Consumer Separation
- Three producers write to the spawn-event queues today: on-hit collision emission (removed here),
  `TimedSpawnSystem` (interval), `StatusProcessSystem` (stack). After this plan, exactly two remain.

## Reused Mechanisms
- Shared spawn-event/expansion/apply pipeline — untouched aside from removing one producer.

## Introduced Mechanisms
- None. Pure subtraction.

## Validation Requirements
- Agents must not run the Unity test runner. Name exact tests (platform/class/method) for the user
  to run; review only user-provided XML under `Logs/`.
- Use `grep` after each task per that task's "Validation" section as the only agent-side check.

## Files / Systems Mentioned By The Plan
See `index.md` Task List table for the authoritative per-task file list.

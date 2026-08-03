# Implementation Context

## Architectural Decisions
- Add continuous projectile as a second archetype/lane, identified by non-enableable `ProjectileContinuousTag`.
- Membership authored by `ProjectileDefinition.continuousCollision`; never inferred from runtime speed.
- Continuous collision and tracking are exclusive: compiler rejects final combination; continuous archetype has no `ProjectileTrackingComponent`.
- Continuous collision uses full-step oriented rectangle; nearest-first hits; no distance clamp or sweep config.

## Global Invariants
- ECS consumers require `ProjectileTag` plus applicable domain/lane tag.
- Reuse pool slots only inside matching archetype; discrete queries exclude continuous tag.
- Lifecycle comments use `ECS Lifecycle:` and change with lifecycle.
- No managed access, per-frame allocation, or structural changes in combat jobs.

## Ownership Boundaries
- Spawn events remain registry-link + instance frame; expansion owns template dereference.
- Expansion routes one event queue into discrete/continuous command lists.
- Apply owns pool allocation/materialization; collision owns collision hit/event production.

## Data Flow
- Authoring flag -> compiled runtime -> `ProjectileSpawnCommand.ContinuousCollision` -> expansion command lane -> matching apply archetype.
- Both collision lanes use target spatial hash and existing hit/event dispatch singleton handles.

## Lifecycle / Allocation Rules
- Pooled entities disable/reuse `Active`; sweep origin seeds on apply and updates before integration.
- Query/native resource owner disposes it. Hot paths use fixed/reusable native storage.

## ECS / Job / Threading Constraints
- Hash consumers depend on `BuildHandle`, then combine into `ConsumerHandle`.
- Producers combine scheduled handles into singleton `ProducerHandle`; sinks complete before draining.
- Explicit arming queries list `ArmingTag`; continuous origin-capture/collision exclude armed entities.

## Determinism Requirements
- Continuous multi-hit candidates retained/resolved nearest-first by closest approach along origin-to-position segment.

## Producer / Consumer Separation
- Do not reach into other system fields; native queues/lists live in explicit singleton components.

## Reused Mechanisms
- AOE two-archetype pool pattern, spawn pipeline, target spatial hash, collision math, shared projectile materialization, validator warnings.

## Introduced Mechanisms
- `ProjectileContinuousTag`, `ProjectileContinuousStepComponent`, `CombatSweepMath`, authored flag/compile validation, continuous command list/apply/movement/collision systems.

## Validation Requirements
- Execute each task's prescribed static/compile/test validation. Stop after any failed or blocked task; update implementation log per task.

## Files / Systems Mentioned By The Plan
- Projectile ECS components/constants, definition/skill compiler/driver/validator, spawn event/expansion/apply, projectile movement/collision, collision math/spatial hash, targeted tests/docs.

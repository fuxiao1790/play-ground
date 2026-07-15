# Implementation Context

## Architectural Decisions
- Promote existing `CombatHitPayload` to a standalone `IComponentData` on all hit source entities.
- Keep `ProjectileHitPayload` as an authoring/config DTO.
- Slim `CombatHitEvent` to `{ Source, Target }`.
- Finalize resolves damage/crit/stack from `ComponentLookup<CombatHitPayload>[hit.Source]`.

## Global Invariants
- Projectile, impact AOE, and lingering AOE entities must all carry `CombatHitPayload`.
- Payload must remain valid until finalize runs; spawn apply/reuse happens after finalize.
- Empty payload hits stay gated out at producers.

## Ownership Boundaries
- Authoring/config/request layers may keep plain payload DTOs unchanged.
- ECS entity representation owns the standalone payload component.
- Collision produces hit events; finalize applies damage and status.

## Data Flow
- Spawn apply materializes payload component on source entity.
- Collision reads source payload for gate only and queues `{ Source, Target }`.
- Finalize drains hit queue and reads source payload through a read-only component lookup.

## Lifecycle / Allocation Rules
- Add `CombatHitPayload` to cold-created and pooled archetypes before `SetComponentData`.
- Do not add/remove payload dynamically on the hot path.

## ECS / Job / Threading Constraints
- Collision jobs read `CombatHitPayload` as `in`.
- Finalize is single-threaded after producer handles complete.
- `ComponentLookup<CombatHitPayload>` in finalize is read-only.

## Determinism Requirements
- Preserve existing target bucketing and crit seed behavior.
- Do not introduce source-order dependent damage changes beyond existing queue order.

## Producer / Consumer Separation
- Producers enqueue only source and target.
- Finalize is the only hit-event consumer.
- Spawn and VFX routing remain separate from damage event data.

## Reused Mechanisms
- Existing `CombatHitPayload` type.
- Existing finalize lookup pattern for target health and stack buffers.
- Existing spawn apply materialization paths.

## Introduced Mechanisms
- Only the `IComponentData` marker on `CombatHitPayload`.
- No new payload type or branch-based source discriminator.

## Validation Requirements
- Build/compile after tasks 001-004 land together.
- Update tests and contract doc in task 005.
- Run relevant Unity test/build commands when available; report any validation that cannot run.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/System/Application/CombatHitPayload.cs`
- `Assets/Scripts/System/Application/CombatHitEvent.cs`
- `Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs`
- `Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs`
- `Assets/Scripts/System/Projectiles/ProjectileCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/AoeEcsComponents.cs`
- `Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs`
- `Assets/Scripts/System/Aoes/AoeCollisionCore.cs`
- `Assets/Scripts/System/Aoes/ImpactAoeCollisionSystem.cs`
- `Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs`
- `Assets/Scripts/System/Combat/Core/CombatRoot.cs`
- `Assets/Tests/PlayMode/AoeSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileCollisionSimulationTests.cs`
- `Assets/Tests/PlayMode/ProjectileTrackingSimulationTests.cs`
- `Assets/Tests/PlayMode/BareMinimumPrototypePlayModeTests.cs`
- `Docs/contracts/combat-hit-and-tick-results.md`

# Detailed DOTS Common + AOE Implementation Plan

## Summary
Move shared combat ECS data into `Assets/Scripts/System/Common/`, migrate projectile internals onto those common components while preserving existing projectile behavior, then replace plain `AoeWorld` with DOTS AOE pulse explosions in the same default ECS world. AOE v1 is single-shape, single-damage-pass explosion only. Projectile impact AOEs resolve on the next simulation step through the managed hit-replay boundary.

## Phase 1: Common ECS Foundation And Projectile Migration
### Milestone 1.1: Common Package
- Create `Assets/Scripts/System/Common/` with namespace `PlayGround.System.Common`.
- Add common data:
  - `CombatKinematicsComponent`: `float2 Position`, `float2 Velocity`.
  - `CombatCollisionComponent`: `CombatShapeType ShapeType`, `Radius`, `HalfExtents`, `RotationRadians`, `BoundsMin`, `BoundsMax`.
  - `CombatHitComponent`: `TargetMask`, `DamageAmount`, `DirectDamageEnabled`, `SourceNodeId`.
  - `CombatTargetElement`: target id/mask, position, shape, bounds.
  - `CombatShapeType`: `Circle`, `Rectangle`, `Capsule`.
- Move neutral helpers to Common:
  - collider shape baking from `ProjectileTargetShapeUtility`.
  - bounds computation from `ProjectileCollisionMath`.
  - narrow-phase shape collision math used by both projectile and AOE.

### Milestone 1.2: Projectile Migration
- Add `ProjectileTag : IComponentData` to projectile entities.
- Replace projectile entity state:
  - `ProjectileKinematicsComponent` -> `CombatKinematicsComponent`.
  - `ProjectileCollisionComponent` -> `CombatCollisionComponent`.
  - `ProjectileHitComponent.HitPayload` fields flatten into `CombatHitComponent`.
- Keep projectile-only components:
  - identity, active tag, tracking, pierce/repeat cooldown, contact gates, child spawning, render tags, render scope, spawn/recycle buffers.
- Every projectile entity query must require `ProjectileTag` or projectile-only identity/scope data so common components alone are never enough.
- Projectile scope entities keep `ProjectileScope`; projectile target buffers stay on projectile scope entities only.
- Preserve behavior for spawning, tracking, movement, child spawning, collision, hit replay, pooling, and render batching.

### Milestone 1.3: Compatibility And Docs
- Update call sites from `ProjectileShapeType` to `CombatShapeType`.
- Keep public gameplay APIs stable where practical: `ProjectileRoot.Spawn(...)`, `ProjectileSpawnCommand`, and `ProjectileHitContext` workflow remain.
- Update docs to describe `System/Common`, domain tags, and projectile use of common components.

### Phase 1 Test Gate
- Existing projectile PlayMode tests pass.
- Add test: entity with common kinematics/collision/hit but no `ProjectileTag` is ignored by projectile systems.
- Keep reuse tests passing:
  - expired projectile entity reused after pool warmup.
  - child-spawner parent keeps stable archetype.
  - contact gates clear on reuse.
- Run Unity build/compile after migration.

## Phase 2: DOTS AOE Pulse System
### Milestone 2.1: AOE ECS Components And Scope
- Replace `AoeWorld` simulation with ECS-backed `AoeRoot`.
- `AoeRoot` owns one AOE scope entity with `AoeScope` and buffers:
  - `AoeSpawnRequestElement`
  - `CombatTargetElement`
  - `AoeHitElement`
  - `AoeRecycleElement`
- Add AOE entity components:
  - `AoeTag`
  - `AoeIdentityComponent`: scope, aoe id, type id.
  - `AoeActiveTag : IEnableableComponent`
  - common `CombatKinematicsComponent`, `CombatCollisionComponent`, `CombatHitComponent`.
  - AOE render data matching projectile-style authored visuals.
- AOE scope and projectile scope stay separate even though both use `CombatTargetElement`.

### Milestone 2.2: AOE Root Boundary
- `AoeRoot.Update()` snapshots managed `IAoeTarget` registry into the scope `CombatTargetElement` buffer.
- `AoeRoot.Spawn(...)` appends spawn requests to the AOE scope buffer.
- ECS AOE systems must not read `AoeTargetRegistry`, GameObjects, Transforms, Colliders, or attack components.
- `AoeRoot.LateUpdate()` submits render batches, drains `AoeHitElement`, maps target ids back to managed targets, and calls `IAoeTarget.ReceiveAoeHit`.

### Milestone 2.3: AOE Systems
- `AoeSimulationSystem`: clears scoped AOE hit buffers at simulation start.
- `AoeSpawnSystem`: drains spawn requests, reuses inactive AOE entities by scope/type, cold-creates only when pool empty.
- `AoeCollisionSystem`: Burst parallel overlap check against scoped `CombatTargetElement`, emits `AoeHitElement`, disables/recycles AOE after first pulse.
- `CombatRenderPrepareSystem`: prepares matrices for batched GPU-instanced AOE
  and projectile visuals.
- Do not add an ECS target-sync system; target sync remains root-owned managed bridge work.

### Milestone 2.4: AOE Authoring
- Keep `AoeAttack` and `AoeRoot.Spawn(AoeSpawnCommand)` as scene-facing APIs.
- AOE type definition bakes from authored prefab:
  - visual sprite/material for rendering.
  - collider shape for hit area.
- V1 ignores lingering behavior:
  - `LifetimeSeconds` and `TickIntervalSeconds` remain on commands for future phases.
  - Pulse AOE damages once and despawns/recycles.

### Phase 2 Test Gate
- AOE pulse hits overlapping target once.
- AOE pulse does not hit outside shape.
- Target mask filters hits.
- AOE hit replay calls target damage callback.
- AOE entity is reused after pulse.
- AOE root creates no per-AOE live damage/collider objects.

## Phase 3: Projectile + AOE Integration
### Milestone 3.1: Cross-Domain Spawn Flow
- Projectile ECS collision emits projectile hit events only.
- AOE ECS collision emits AOE hit events only.
- Domain ECS systems do not write spawn requests into another domain's buffers.
- Problem to solve:
  - Projectiles are data-runtime entities, not live attack objects.
  - `ProjectileAttack` and source actor behavior can change while older projectiles are still in flight.
  - Hit-time callbacks to the live source would force the source to handle multiple historical behavior versions.
  - Current direct damage avoids this by snapshotting damage onto the projectile at fire time; hit-spawn behavior must follow the same rule.
- Phase 3 design decision: use fat fire-time snapshot payloads for hit-spawn behavior.
  - Option A, fat snapshot payloads:
    - more memory.
    - more copying.
    - better lifetime safety.
    - faster collision/spawn path.
  - Option B, runtime lookup or pointer chase:
    - smaller payloads.
    - less duplicated data.
    - more cache misses.
    - harder lifetime and versioning rules.
  - Option C, managed authoring callback:
    - flexible.
    - editor-friendly.
    - unsafe lifetime.
    - poor Job/Burst compatibility.
    - can be dead or mutated before hit replay.
  - Choose Option A because it is easiest to implement safely: all hit-spawn decisions are made when authored attack objects are guaranteed alive.
  - Option A also matches current direct damage behavior, where damage and direct-damage enablement are snapshotted onto the projectile at fire time.
  - Memory cost is controlled by how hit-spawn payloads are authored and used, not by introducing runtime lookup/version management in v1.
- Add fire-time hit-spawn snapshot data:
  - `ProjectileAttack` authoring writes any needed impact AOE or projectile-burst data into `ProjectileSpawnCommand` when it fires.
  - `ProjectileSpawnCommand`, projectile ECS components, and `ProjectileHitElement` carry only plain copied data needed to emit future commands, such as effect kind, type ids, damage, count, spread, lifetime, target mask, routing/team data, and source id.
  - Projectile ECS treats hit-spawn data as copied payload data. It does not call live managed authoring objects or resolve external effect tables during collision.
  - The snapshot does not contain live `ProjectileAttack`, `AoeAttack`, `GameObject`, `Transform`, `Collider2D`, or target references.
- `ProjectileRoot.LateUpdate()` drains projectile hits and replays managed callbacks.
- `AoeRoot.LateUpdate()` drains AOE hits and replays managed callbacks.
- Managed replay reads copied hit-spawn snapshot payloads and emits typed combat spawn commands:
  - projectile-to-AOE impact commands.
  - AOE-to-projectile burst commands.
  - future domain-to-domain effect commands.
- A managed combat spawn router, wired by `GameRoot`, maps typed spawn commands to the correct scoped root:
  - player projectile effects that target mobs route to `AoeRoot_PlayerToMob`.
  - mob projectile effects that target the player route to `AoeRoot_MobToPlayer`.
  - player AOE effects that target mobs route to `ProjectileRoot_PlayerToMob`.
  - mob AOE effects that target the player route to `ProjectileRoot_MobToPlayer`.
- Destination roots append received spawn commands to their own scoped request buffers through public APIs such as `ProjectileRoot.Spawn(...)` and `AoeRoot.Spawn(...)`.
- Cross-domain spawn commands carry only snapshot data such as position, direction, damage, source id, target mask, and template/type id. They do not carry live `GameObject`, `Transform`, `Collider2D`, or target references.
- Cross-domain spawns resolve on the next ECS simulation step because requests are submitted after the current simulation has completed.
- Cross-domain recursion is prevented by command shape, not by a runtime depth value:
  - Only authored attack spawn paths may attach cross-domain hit-spawn payloads.
  - Cross-domain router output commands must use non-chaining spawn payloads.
  - A projectile spawned by an AOE burst cannot carry an impact-AOE payload.
  - An AOE spawned by a projectile impact cannot carry an AOE-to-projectile payload.
  - Do not add `CrossDomainSpawnDepth`, recursion counters, or similar runtime state to make this safe.
  - If future content needs deeper chains, add an explicit authored combo system instead of allowing combat domains to recursively spawn each other.

### Milestone 3.2: Mixed Runtime Safety
- Projectile and AOE roots share `World.DefaultGameObjectInjectionWorld`.
- Domain separation rules:
  - projectile entities carry `ProjectileTag`.
  - AOE entities carry `AoeTag`.
  - projectile scope entities carry `ProjectileScope`.
  - AOE scope entities carry `AoeScope`.
  - common components alone never make an entity eligible for domain systems.
- Add AOE counters:
  - active, spawned, despawned/reused, hit events, render batches.
- Update docs to state AOE is DOTS, not plain `AoeWorld`.

### Phase 3 Test Gate
- Projectile impact AOE damages valid target on next step.
- Projectile direct damage disabled still allows impact AOE damage.
- Projectile hit-spawn behavior is snapshotted at fire time: changing the source attack after firing does not change existing in-flight projectile impact behavior.
- Projectile hit-spawn replay does not call back into live `ProjectileAttack`, source actor behavior, or runtime effect lookup tables to decide which effect to emit.
- Cross-domain spawned projectiles/AOEs cannot carry cross-domain hit-spawn payloads, making projectile-to-AOE-to-projectile and AOE-to-projectile-to-AOE chains unrepresentable.
- Direct `AoeAttack` and projectile impact AOE both use same AOE root path.
- Player projectile root and player AOE root share one ECS world but use different scope entities.
- Mixed projectile + AOE stress smoke test: counters increment, no `AoeWorld` runtime path remains, scoped AOE entity count stays constant after warmup.

## Assumptions
- Common package path is exactly `Assets/Scripts/System/Common/`.
- AOE v1 is pulse explosion only: one overlap pass, one damage event per overlapping target, then recycle.
- No AOE child spawning, lingering tick gates, re-entry rules, or same-step impact AOE in this work.
- `AoeWorld` contact-state behavior is intentionally dropped in v1; each pulse AOE can hit each overlapping target once per spawn.
- Runtime correctness and performance boundaries matter more than preserving old internal component names.

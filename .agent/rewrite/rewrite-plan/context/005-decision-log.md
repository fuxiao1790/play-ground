# Context 005 — Decision Log

Settled decisions. **Do not reopen these during implementation.** Each task that touches the area must follow the decision.

## Governing principle (overrides the prior revision of this plan)

The rewrite's single highest priority is a **sound future architecture** that faithfully realizes the direction (`.agent/rewrite/design.md` / `design-points.md`). The old systems mostly *work*; what is unacceptable is the *architecture*. Therefore:

> **Preserve old logic, not old architecture.** Churn is acceptable. Temporary breakage of working behavior during the rewrite is acceptable, provided the end-state architecture matches the direction. "It already works / merging is wide churn / it's out-of-scope to rename" is **not** a valid reason to diverge from the direction.

The previous revision of this plan made several decisions that traded architectural fidelity for reduced churn. Those are **reversed** here and superseded:

| Old decision | Status | Superseded by |
|---|---|---|
| D-NAMING (keep managed `...SpawnCommand`; ECS uses `...CommandData`) | **reversed** | D-NAMING-EVENTCMD |
| D1 (keep `TargetId`+`Faction`; no proxy entities) | **reversed** | D-PROXY-ENTITY |
| D2 (keep per-domain active tags; defer generic `Active`) | **reversed** | D-ACTIVE-GENERIC |
| D-SHAPE-BUCKETING (one bucketed apply system) | **reversed** | D-SHAPE-EXPLICIT |
| D-DAMAGE-CLEAR (single `CombatDamageElement` clear owner) | **obsolete** | D-DAMAGE-TRANSPORT (the buffer is removed) |

Unchanged and still in force: D-EXPANSION-OWNS-MATH, D-TRANSPORT, D-CONVERT-RELOCATE, D-LIFETIME-PULSE, D-LIFETIME-VFX, D-AOE-EXPANSION-MINIMAL.

---

```
Decision D-NAMING-EVENTCMD: The Event/Command vocabulary is load-bearing (design R3) and the
             names must match meaning. A type that carries multiplicity (Count / SpreadDegrees /
             JitterDegrees / JitterSeed) is gameplay INTENT and is named ...SpawnEvent. A type
             that describes exactly one concrete entity is allocation intent and is named
             ...SpawnCommand (NO "Data" suffix).
Finding: the managed authoring type ProjectileSpawnCommand
         (System/Projectile/ProjectileSpawnCommand.cs) carries Count, SpreadDegrees, JitterDegrees
         — it is therefore semantically an EVENT, not a command. Naming it "Command" is the exact
         vocabulary inversion R3 forbids.
Decision:
  - The single multiplicity-bearing intent type is ProjectileSpawnEvent / AoeSpawnEvent
    (blittable, IBufferElementData + NativeQueue element). This is what authoring builds, what
    CombatRoot submits, and what flows through the producer queue/scope buffer.
  - The post-expansion one-entity type is ProjectileSpawnCommand / AoeSpawnCommand (blittable,
    native-container only). The "...CommandData" name is retired.
  - The managed authoring DTOs (today named ProjectileSpawnCommand / AoeSpawnCommand) are renamed
    to ProjectileSpawnRequest / AoeSpawnRequest — the authoring-layer request a caller fills in
    (keeps UnityEngine.Vector2 / managed snapshot fields + constructors). CombatRoot.Spawn converts
    a ...SpawnRequest into the blittable ECS ...SpawnEvent at the managed↔ECS boundary.
    NOTE: "Request" is the managed authoring layer only; the deleted dual-use
    ProjectileSpawnRequestElement is unrelated and gone — do not resurrect it.
Three distinct names, no overlap: ProjectileSpawnRequest (managed authoring intent) →
              ProjectileSpawnEvent (blittable ECS intent + multiplicity) → ProjectileSpawnCommand
              (blittable ECS, one entity). Same triple for AoE.
Churn accepted: Mob/MobProjectileAttack.cs, Skills/SkillSpawnTranslator.cs, Mob/MobRoot.cs,
                CombatRoot.cs, and all tests that construct these types are updated. This is
                exactly the churn the prior D-NAMING avoided; per the governing principle it is
                now in scope.
Consequences: searching "...SpawnCommand" now unambiguously means the single-entity allocation
              type; "...SpawnEvent" always means multiplicity-bearing intent.
Affected tasks: 002, 003, 004, 005, 006, plus the authoring-rename task.
```

```
Decision D-PROXY-ENTITY: Adopt per-target ECS proxy entities with a managed companion
             (design §8.2 / §8.3 / §8.4). Damage is keyed by Entity, not (TargetId + Faction).
Context: design §8 specifies proxy entities and DamageReplayEvent.TargetProxy:Entity, while §1/§11
         say "refine the target bridge, do not rebuild." The direction is internally contradictory
         here. RESOLUTION (explicit user direction): §8 wins — the direction wants GameObjects
         represented in ECS as entities with a companion back to the GameObject. The "do not
         rebuild" reading was the churn-avoidance interpretation and is overridden.
Decision:
  - Each ICombatTarget GameObject owns an ECS proxy Entity. The proxy carries the unmanaged target
    state used by simulation (TargetPosition, TargetCollisionShape, TargetFaction) AND a managed
    companion: `class TargetCompanion : IComponentData { public ICombatTarget Target; }`.
  - There is NO separate alive/enableable flag on the proxy: proxy existence == targetable. Leaving
    simulation means deleting the proxy (§8.3), not disabling it.
  - The proxy is created via a cached-archetype helper `CombatTargetProxy` (System/Common) so the
    archetype is built once, not per target.
  - Lifecycle is fixed: the GameObject creates the proxy on enable; pushes TargetPosition +
    TargetCollisionShape into the proxy in Update() (before ECS simulation); and DELETES the proxy
    in LateUpdate() (after damage dispatch) when it stops simulating — not disabled, not pooled
    (§8.3). Death animation may continue on the GameObject after proxy deletion.
  - DamageReplayEvent carries Entity TargetProxy (replaces TargetId + Faction as identity).
    Collision writes the proxy entity it hit.
  - Only DamageDispatchBridge reads the managed companion (§8.4); no Burst/simulation system may.
  - The CombatTargetElement-buffer sync (CombatTargetSync / CombatTargetSyncSystem) is replaced by
    proxy creation + per-frame position push. Spatial queries in collision read proxy entities
    (via a query/spatial hash over proxy components) instead of the synced buffer.
Logic preserved: crit roll on the main thread, atomic-per-hit replay, group-by-target dispatch,
                 ReceiveHits call shape, the spatial-hash collision math. Only the IDENTITY model
                 and the sync mechanism change.
Churn accepted: target registry/sync, collision target source, DamageReplayEvent shape, dispatch
                resolution, ICombatTarget surface (gains proxy-lifecycle hooks), and tests.
Affected tasks: the new target-proxy task(s), 008 (damage), 004/006 (collision emits Entity).
```

```
Decision D-ACTIVE-GENERIC: Adopt the generic occupancy flag
             `public struct Active : IComponentData, IEnableableComponent {}` (design §3) shared by
             all reusable runtime entities. Replace the per-domain ProjectileActiveTag /
             AoeActiveTag.
Rationale: §3 is explicit: occupancy is ONE generic enableable flag; slot KIND is component
           presence; reuse is WithDisabled<Active>() + the archetype's marker components. Two
           differently-named flags that mean the same thing is the incidental duplication the
           rewrite removes. The prior D2 kept them solely to avoid churn — overridden.
Decision:
  - Introduce Active. Projectile and AoE occupancy both use it. Reuse queries become
    WithDisabled<Active>() + ProjectileTag / AoeTag (+ shape markers).
  - Domain gating still uses the domain marker components (ProjectileTag / AoeTag), so common
    components alone never opt an entity into a domain system (Global Invariant: domain gating).
  - CombatRenderActiveTag (render-pipeline opt-in) and the collision opt-in tags are a DIFFERENT
    concept (per-feature participation, not slot occupancy) and stay as their own enableable
    components. Only the occupancy flag is unified.
Churn accepted: collision, lifetime, spawn apply, tracking, child-spawn, render, CombatRoot, and
                every test that references ProjectileActiveTag / AoeActiveTag (~30 files).
Affected tasks: the new generic-Active task (runs early, before the apply rebuild), and every task
                whose archetype/query references an active tag.
```

```
Decision D-DAMAGE-TRANSPORT: Damage replay uses the native-container path of design §8.1:
             collision writes DamageReplayEvent into a NativeQueue<DamageReplayEvent>.ParallelWriter
             (decisive — NOT NativeStream; §8.1 names NativeQueue and the spawn pipeline already
             uses queue→array); after both collisions it is finalized to a frozen
             NativeArray<DamageReplayEvent> that DamageDispatchBridge consumes, then the queue is
             Clear()'d by its owner (design §6). The scope DynamicBuffer<CombatDamageElement> AND
             CombatHitFlushJob are REMOVED (design §11 lists CombatHitFlushJob as replaced).
Rationale: §8.1 explicitly prescribes NativeQueue → NativeArray → dispatch "consistent with the
           spawn/consequence transport rules (§6)." The prior plan kept the DynamicBuffer + flush
           job purely to avoid reshaping a working path. The flush stage and the buffer are exactly
           the indirection §11 removes.
Decision:
  - DamageReplayEvent { Entity TargetProxy; DamageSnapshot Damage; float2 HitPosition;
    float2 HitDirection; + the carried fields needed for crit roll / stack effect / source }.
    Keyed by Entity (see D-PROXY-ENTITY), blittable.
  - Collision enqueues into the damage queue. A finalize step freezes it to a NativeArray.
    DamageDispatchBridge reads the array, resolves Entity → companion → GameObject, replays, and the
    queue is Clear()'d by its owner that frame.
  - CombatHitFlushJob, CombatDamageElement, and the double-clear (D-DAMAGE-CLEAR) all go away. No
    per-frame buffer to clear; the queue's empty-before-write / clear-after-read contract (§6)
    replaces the OrderFirst clears.
Logic preserved: crit roll on main thread, atomic per-hit, group-by-target, one ReceiveHits per
                 target group.
Affected tasks: 008 (now a transport reshape, not a rename), 004/006 (collision write type),
                ordering task (queue phase discipline replaces the clear).
```

```
Decision D-SHAPE-EXPLICIT: Start explicit (design §5.3): each REAL spawn shape gets its own command
             container and its own apply system. Do NOT route all shapes through one bucketed apply
             system keyed by a runtime shape field.
Rationale: §5.3 — "command queue == required component set == dead-slot query shape == overflow
           creation shape", and "shape is selected during expansion, never re-derived from a mask in
           apply." A single apply system that branches on HasChildSpawner re-derives shape in apply,
           which is the boundary leak §5.3/§5.4 forbid. The prior D-SHAPE-BUCKETING merged them to
           avoid writing more systems — overridden.
Decision:
  - Expansion classifies each resolved entity into the exact-shape command container for the shape
    that actually exists in the codebase today (projectile: with / without child-spawner; AoE: the
    single AoE shape). Each container has a dedicated apply system whose reuse query, init, and
    overflow AddComponent set are fixed for that one shape.
  - Still DO NOT build apply systems for shapes that have no producer (the §5.3 "generalize only
    after real duplication / don't build on speculation" rule is kept — it cuts speculative shapes,
    NOT real ones).
Consequences: apply systems contain no shape-key branch; adding a new real shape adds a container +
              apply system rather than a new switch case.
Affected tasks: 003 (projectile apply), 005 (aoe apply).
```

---

## Decisions carried over unchanged

```
Decision D-EXPANSION-OWNS-MATH: ProjectileSpawnExpansionSystem owns ALL spawn math (count, spread,
             jitter, direction, position, rotation, velocity, world bounds, render-Z, per-shot id).
             ProjectileSpawnCommand has NO Count/Spread/Jitter/BaseDirection/Speed.
Rationale: design R4 / §5.4. Apply is a pure copy of resolved fields into components.
Affected tasks: 002, 003, 004, 005, 006.
```

```
Decision D-TRANSPORT: High-volume internal producers write a system-owned
             NativeQueue<...SpawnEvent>.ParallelWriter; the low-volume managed submission
             (CombatRoot.Spawn) appends to a scope DynamicBuffer<...SpawnEvent>. Expansion finalizes
             BOTH into one frozen NativeArray each frame and clears both.
Rationale: design §6 wants the high-volume path native and explicitly allows scope entities as
           low-volume submission markers.
Consequences: ...SpawnEvent implements IBufferElementData AND is used as a NativeQueue element.
Affected tasks: 002, 003, 004, 005, 006.
```

```
Decision D-CONVERT-RELOCATE: Delete CombatSpawnConvertJob and CombatPendingSpawn. Relocate its
             request-building logic into shared helpers (ProjectileSpawnPipeline.BuildImpact*,
             AoeSpawnPipeline.BuildImpactAoe) called by the collision jobs, which emit the final
             typed events directly (design §7.2).
Rationale: collision is the final producer of typed consequence events; the generic
           CombatPendingSpawn + re-interpretation pass is the indirection the rewrite removes.
Consequences: the convert logic (HashId salts, DirectionFromTo invert rules, seed-contact-gate,
              tracking/render builders) is relocated VERBATIM to preserve behavior.
Affected tasks: 004 (projectile), 006 (aoe), 007 (delete dead code).
```

```
Decision D-LIFETIME-PULSE: Unify lifetime via an enableable CombatLifetimeComponent. Pulse AOEs are
             spawned with it DISABLED; the unified CombatLifetimeSystem skips them; AoE collision
             deactivates pulse AOEs the same tick (unchanged). The unified system operates over the
             generic Active flag (D-ACTIVE-GENERIC), not per-domain tags.
Rationale: design §4 + R2 (component/enable-state = behavior).
Affected tasks: 001.
```

```
Decision D-LIFETIME-VFX: The unified CombatLifetimeSystem emits despawn VFX (Trigger=2) using
             area = max(render.VisualScale.x, render.VisualScale.y) for BOTH domains. Pulse VFX
             (Trigger=3) lives in a separate AoE-only AoePulseVfxSystem (a legitimate separate
             concern, consistent with R2).
Rationale: keeps the unified system domain-neutral (§4).
Consequences: AoE despawn-VFX area source changes from AoeAreaComponent.Size to render scale — an
              intentional minor visual change; fallback: Aoe apply copies AreaSize into
              render.VisualScale so the values match.
Affected tasks: 001.
```

```
Decision D-AOE-EXPANSION-MINIMAL: AoeSpawnExpansionSystem exists for structural parity with the
             projectile pipeline (design §5.7) but performs a 1:1 transform (resolve world bounds,
             carry spawn VFX). No scatter/fan-out is implemented now; the stage shape supports adding
             it without restructuring.
Rationale: §5.7 requires AoE to use the identical pipeline structure; no current AoE behavior fans
           out, so building scatter now would be speculative.
Affected tasks: 005, 006.
```

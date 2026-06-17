# Context 005 — Decision Log

Settled decisions made during planning. **Do not reopen these during implementation.** Each task that touches the area must follow the decision.

---

```
Decision D-NAMING: ECS spawn types are named ProjectileSpawnEvent / ProjectileSpawnCommandData
                   (and AoeSpawnEvent / AoeSpawnCommandData). The managed authoring types
                   ProjectileSpawnCommand and AoeSpawnCommand keep their names.
Rationale: The managed ProjectileSpawnCommand / AoeSpawnCommand are public API referenced by
           Mob/MobProjectileAttack.cs, Skills/SkillSpawnTranslator.cs, and CombatRoot.cs.
           Renaming them is broad churn outside this rewrite's scope. The ECS command type
           therefore cannot reuse the name "ProjectileSpawnCommand" (same namespace). The
           "Data" suffix keeps the design's "Command" vocabulary while staying unambiguous.
Alternatives considered: (a) rename managed types; (b) put ECS types in a sub-namespace.
Rejected because: (a) out-of-scope churn across attack/skill code; (b) two same-named types
                  imported together is a readability trap for the lower-cost agent.
Consequences: search-and-replace safety — "ProjectileSpawnCommand" without "Data" always
              means the managed authoring struct.
Affected tasks: 002, 003, 004, 005, 006.
```

```
Decision D1: Damage replay keeps the (TargetId + CombatFaction) identity model. Per-target
             ECS proxy entities and DamageReplayEvent.TargetProxy:Entity (design §8.2/8.3) are
             OUT OF SCOPE.
Rationale: Design §1 out-of-scope and §11 both say the target bridge is "refine the boundary,
           do not rebuild"; §12 lists the hybrid/ECS-owned-target model as a later direction.
           No per-target proxy entities exist today; targets are synced into CombatTargetElement
           and damage is replayed via TargetId→dictionary, which already satisfies §8.1's
           atomic-per-hit, group-by-target contract.
Alternatives considered: implement proxy entities + Entity-keyed DamageReplayEvent now.
Rejected because: it is a full target-bridge rebuild the design explicitly defers.
Consequences: DamageReplayEvent carries TargetId + Faction (rename of CombatPendingDamage);
              CombatHitDispatchSystem becomes DamageDispatchBridge with unchanged behavior.
Affected tasks: 008.
```

```
Decision D2: Keep per-domain enableable active tags (ProjectileActiveTag, AoeActiveTag). The
             generic single Active flag (design §3) is DEFERRED to a follow-up refactor.
Rationale: The existing per-domain tags already realize §3's functional intent — occupancy is an
           enableable flag, slot kind is component presence, reuse is a WithDisabled<...> query.
           Merging into one Active is wide, mechanical churn (collision, lifetime, spawn,
           tracking, child-spawn, render, CombatRoot, every test) with no behavioral or
           capability gain, and the rewrite's primary goal is clarity of the spawn/event/damage
           cluster, not tag consolidation.
Alternatives considered: introduce generic Active now as a mechanical rename task.
Rejected because: risk/blast-radius outweighs benefit for the stated goal.
Consequences: CombatLifetimeComponent is the only newly-shared occupancy-adjacent component;
              lifetime unification operates per-domain over each domain's active tag.
Affected tasks: 001 (lifetime), and a documented future follow-up (not in this plan).
```

```
Decision D-EXPANSION-OWNS-MATH: ProjectileSpawnExpansionSystem is the single owner of ALL spawn
             math (count, spread, jitter, direction, position, rotation, velocity, world bounds,
             render-Z, per-shot id). ProjectileSpawnCommandData has NO Count/Spread/Jitter/
             BaseDirection/Speed fields.
Rationale: design R4 / §5.4. Apply must be a pure copy of resolved fields into components.
Alternatives considered: keep multiplicity on the command and let apply branch on Count.
Rejected because: that is exactly today's dual-use smell the rewrite removes.
Consequences: the impact-projectile/burst build helper sets BaseDirection/Speed/Count/Spread on
              the EVENT; expansion consumes them. Apply never sees them.
Affected tasks: 002, 003, 004, 005, 006.
```

```
Decision D-TRANSPORT: High-volume internal producers write a system-owned
             NativeQueue<...SpawnEvent>.ParallelWriter; the low-volume managed submission
             (CombatRoot.Spawn) appends to a scope DynamicBuffer<...SpawnEvent>. The expansion
             system finalizes BOTH into one frozen NativeArray each frame.
Rationale: design §6 wants the high-volume path native; it explicitly allows singleton/scope
           entities as low-volume submission markers. CombatRoot already submits via a scope
           buffer on the main thread — preserving that path is the lowest-risk integration.
Alternatives considered: route managed submission through the NativeQueue too.
Rejected because: MonoBehaviour→system enqueue timing is more fragile than the proven buffer
                  append, for no throughput gain at attack-fire volume.
Consequences: ...SpawnEvent implements IBufferElementData AND is used as a NativeQueue element.
              Expansion drains queue + buffer and clears both.
Affected tasks: 002, 003, 004, 005, 006.
```

```
Decision D-SHAPE-BUCKETING: Keep shape-keyed bucketing for apply instead of building a distinct
             command queue + apply system per theoretical shape (literal §5.3).
Rationale: only two real projectile shapes exist (with / without child-spawner, selected by
           HasChildSpawner) and one AoE shape. Design §5.3 says "Start explicit. Generalize only
           after real duplication appears — not on speculation." The existing bucket key
           (faction, typeId, hasChildSpawner) already gives "command queue == required component
           set == dead-slot query shape == overflow creation shape" per bucket.
Alternatives considered: BasicProjectileCommandQueue / TimedChildProjectileCommandQueue / ... as
             separate systems.
Rejected because: speculative system proliferation the design warns against.
Consequences: shape is selected at expansion (HasChildSpawner carried on the command) and never
              re-derived in apply; apply keeps the proven bucket/reuse/cold-create machinery.
Affected tasks: 003, 005.
```

```
Decision D-CONVERT-RELOCATE: Delete CombatSpawnConvertJob and CombatPendingSpawn. Relocate its
             request-building logic into shared helpers (ProjectileSpawnPipeline.BuildImpact*,
             AoeSpawnPipeline.BuildImpactAoe) called by the collision jobs, which now emit the
             final typed events directly.
Rationale: design §7.2 — collision is the final producer of typed consequence events; the generic
           CombatPendingSpawn + re-interpretation pass is the indirection the rewrite removes.
Alternatives considered: keep CombatPendingSpawn but make it typed.
Rejected because: it keeps an extra stream + job stage for no benefit once events are typed.
Consequences: the convert logic (HashId salts, DirectionFromTo invert rules, seed-contact-gate,
              tracking/render builders) must be relocated VERBATIM to preserve behavior.
Affected tasks: 004 (projectile), 006 (aoe), 007 (delete dead code).
```

```
Decision D-LIFETIME-PULSE: Unify lifetime via an enableable CombatLifetimeComponent. Pulse AOEs
             are spawned with it DISABLED; the unified CombatLifetimeSystem skips them; AoE
             collision deactivates pulse AOEs the same tick (unchanged).
Rationale: design §4 + R2 (component/enable-state = behavior). Replaces the AoeLifetimeComponent
           IsPulse branch with an enable-state, so the generic system needs no AoE knowledge.
Alternatives considered: keep an IsPulse int on a shared component and branch in the unified job.
Rejected because: that re-introduces a flag-branch the design (R2) rejects.
Consequences: Aoe apply must enable/disable CombatLifetimeComponent based on Lifetime<=0; the
              old AoeLifetimeComponent.IsPulse is removed (its only other reader was the pulse-skip
              and the pulse VFX job, which moves to AoePulseVfxSystem keyed on AoePulseVfxComponent).
Affected tasks: 001.
```

```
Decision D-LIFETIME-VFX: The unified CombatLifetimeSystem emits despawn VFX (Trigger=2) using
             area = max(render.VisualScale.x, render.VisualScale.y) for BOTH domains. Pulse VFX
             (Trigger=3) moves to a separate AoE-only AoePulseVfxSystem.
Rationale: keeps the unified system domain-neutral (§4). Projectile already uses render scale.
Alternatives considered: have the unified system read AoeAreaComponent when present.
Rejected because: that couples the generic system to an AoE component.
Consequences: AoE despawn-VFX area source changes from AoeAreaComponent.Size to render scale.
              This is an INTENTIONAL minor visual change (the two usually track each other);
              flagged for the validation pass to confirm it looks acceptable. If unacceptable,
              fallback: Aoe apply copies AreaSize into render.VisualScale so the values match.
Affected tasks: 001.
```

```
Decision D-DAMAGE-CLEAR: Exactly one OrderFirst system clears the scope CombatDamageElement
             buffer (ProjectileSimulationSystem). AoeSimulationSystem stops clearing it.
Rationale: both currently clear the same buffer (CombatDamageElement) at OrderFirst — redundant,
           and order among OrderFirst systems is not guaranteed, so the dependency is implicit.
Alternatives considered: a dedicated CombatDamageClearSystem.
Rejected because: an extra system for one Clear() is unnecessary; ProjectileSimulationSystem
                  already owns this responsibility.
Consequences: AoeSimulationSystem either keeps another responsibility or becomes empty; if empty,
              fold its remaining work and remove it (verify no other clear/setup is lost first).
Affected tasks: 008.
```

```
Decision D-AOE-EXPANSION-MINIMAL: AoeSpawnExpansionSystem exists for structural parity with the
             projectile pipeline (design §5.7) but performs a 1:1 transform (resolve world bounds,
             carry spawn VFX). No scatter/fan-out is implemented now.
Rationale: design §5.7 requires AoE to use the identical pipeline structure, but no current AoE
           behavior fans out; building scatter now would be speculative.
Alternatives considered: skip AoE expansion and apply AoE events directly.
Rejected because: it would diverge AoE from the projectile pipeline shape the design mandates,
                  making future AoE scatter a special case again.
Consequences: a thin but real expansion stage; future AoE scatter slots in without restructuring.
Affected tasks: 005, 006.
```

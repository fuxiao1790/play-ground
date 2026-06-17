# Refactor Plan Index

ECS Combat Rewrite — spawn / event / damage / lifetime cluster.
Source design: `.agent/rewrite/design.md`. Grounded against the real code under `Assets/Scripts/System/`.

## Purpose
Replace the accreted spawn/event/damage cluster with a phase-oriented pipeline that has explicit ownership boundaries (design's primary goal: **architecture clarity**). Concretely:
- split spawn **intent** (`...SpawnEvent`, may be multi-shot) from **allocation intent** (`...SpawnCommandData`, exactly one entity) — design R3;
- make a single **expansion** phase own all spawn math, leaving **apply** a pure copy — R4 / §5.4;
- replace the dual-use `ProjectileSpawnRequestElement` + `ProjectileMultiExpandSystem` + generic `CombatSpawnConvertJob` with typed events and native-container phase discipline — §5 / §6 / §7.2;
- **unify** projectile + AoE lifetime into one generic system — §4;
- reshape the damage replay path to the contract vocabulary (`DamageReplayEvent` / `DamageDispatchBridge`) — §8.

## Planning Assumptions
- The managed authoring types `ProjectileSpawnCommand` / `AoeSpawnCommand` are public API used by attack/skill code. — **Confirmed by codebase** (`Mob/MobProjectileAttack.cs`, `Skills/SkillSpawnTranslator.cs`, `CombatRoot.cs`).
- The dual-use `ProjectileSpawnRequestElement` + `ProjectileMultiExpandSystem` + `CombatSpawnConvertJob` are the "spawn mess" of design §12. — **Inferred from both** (its own "Dual-use" comment + the R3/R4 violation).
- Contact-gate systems already match the design's narrowed "gate-state maintenance only" role. — **Confirmed by codebase** (`ProjectileContactGateSystem`/`AoeContactGateSystem` only tick + compact).
- Collision already emits separated damage/spawn/vfx streams; only the spawn stream is generic. — **Confirmed by codebase** (`ProjectileCollisionSystem` writes three `NativeStream`s).
- Per-target ECS proxy entities (design §8.2/8.3) do **not** exist; targets are synced into `CombatTargetElement` and damage replays via `TargetId`+`Faction`. — **Confirmed by codebase**.
- The existing per-domain active tags already realize design §3's functional intent (occupancy = enableable flag; kind = component presence; reuse = `WithDisabled<>` query). — **Confirmed by codebase**.
- Unifying AoE despawn VFX area onto render scale is an acceptable minor visual change. — **Risky assumption** (flagged in D-LIFETIME-VFX with a documented fallback).

## Target Architecture Summary
Decisive end-state (full detail: `context/002-target-architecture.md`).

- **Ownership boundaries:** producers create intent only; one expansion system per domain owns all spawn math; one apply system per domain owns allocation/reuse; collision is the final producer of typed consequence events; `DamageDispatchBridge` is the only reader crossing to managed target callbacks.
- **Data flow:** producers → `NativeQueue<...SpawnEvent>` (internal) + scope `DynamicBuffer<...SpawnEvent>` (managed submission) → expansion finalizes to a frozen `NativeArray`, fans out → `...SpawnCommandData` → apply reuses disabled slots / cold-creates.
- **Event/data shapes:** `ProjectileSpawnEvent` / `AoeSpawnEvent` (intent + multiplicity), `ProjectileSpawnCommandData` / `AoeSpawnCommandData` (resolved single entity, no multiplicity), `DamageReplayEvent` (atomic per-hit, `TargetId`+`Faction`), `CombatLifetimeComponent` (enableable, unified).
- **System responsibilities:** `CombatLifetimeSystem` (unified countdown/disable), `TimedProjectileSpawnSystem` (timed producer), `Projectile/AoeSpawnExpansionSystem`, `Projectile/AoeSpawnApplySystem`, `AoePulseVfxSystem`, `DamageDispatchBridge`. Collision/movement/tracking/gates/VFX/render preserved.
- **Ordering/phase rules:** design §9 codified in `context/004-system-ordering.md`; next-tick spawn (R5) enforced by order only.
- **Compatibility/migration:** new pipeline built beside the old per domain, then cut over, then old deleted — every task compiles.
- **Removed pathways:** dual-use `ProjectileSpawnRequestElement`, `ProjectileMultiExpandSystem`, `ProjectileSpawnSystem`, `ProjectileChildSpawnSystem`, `AoeSpawnSystem`, `CombatSpawnConvertJob`, `CombatPendingSpawn`, `ProjectileLifetimeSystem`, `AoeLifetimeSystem`, `ProjectileLifetimeComponent`, `AoeLifetimeComponent`, `AoeSpawnRequestElement`, the duplicate damage-buffer clear.

## Current Codebase Findings
| File | Symbols | Current responsibility | Disposition | Why it matters |
|---|---|---|---|---|
| `System/Projectile/ProjectileEcsComponents.cs` | `ProjectileSpawnRequestElement` | dual-use intent+command+buffer | **removed** (T004) | the core R3/R4 violation; replaced by event+command |
| `System/Projectile/ProjectileMultiExpandSystem.cs` | system + expand job | fan out Count>1 | **removed** (T004) | folded into `ProjectileSpawnExpansionSystem` |
| `System/Projectile/ProjectileSpawnSystem.cs` | system + reuse job | bucket/reuse/cold-create | **moved** → `ProjectileSpawnApplySystem` (T003/T004) | proven reuse machinery kept, input changed |
| `System/Projectile/ProjectileChildSpawnSystem.cs` | timed child producer | ECB-append child requests | **moved** → `TimedProjectileSpawnSystem` (T004) | producer now enqueues typed events |
| `System/Common/CombatSpawnConvertJob.cs` | convert job | generic pending-spawn → requests | **removed** (T007) | logic relocated to typed build helpers |
| `System/Common/CombatHitElement.cs` | `CombatPendingSpawn`, `CombatPendingDamage` | transient consequence/damage | `CombatPendingSpawn` **removed** (T007); `CombatPendingDamage` **renamed** `DamageReplayEvent` (T008) | typed consequence events; §8 vocabulary |
| `System/Common/CombatHitDispatchSystem.cs` | dispatch system | replay damage to targets | **renamed** `DamageDispatchBridge` (T008) | §8.4 controlled boundary |
| `System/Aoe/AoeSpawnSystem.cs` | system | drain+bucket+reuse AoE | **moved** → `AoeSpawnExpansionSystem`+`AoeSpawnApplySystem` (T005/T006) | §5.7 identical pipeline |
| `System/Aoe/AoeEcsComponents.cs` | `AoeSpawnRequestElement`, `AoeLifetimeComponent` | buffer + lifetime | `AoeSpawnRequestElement` **removed** (T006); `AoeLifetimeComponent` **removed** (T001) | event split + unified lifetime |
| `System/Projectile/ProjectileLifetimeSystem.cs`, `System/Aoe/AoeLifetimeSystem.cs` | lifetime systems | countdown/disable/VFX | **removed** → `CombatLifetimeSystem` + `AoePulseVfxSystem` (T001) | §4 unification |
| `System/Projectile/ProjectileSimulationSystem.cs`, `System/Aoe/AoeSimulationSystem.cs` | OrderFirst clears | both clear `CombatDamageElement` | consolidate to one owner (T008); AoE sim possibly deleted | redundant double-clear |
| `System/Common/CombatRoot.cs` | `Spawn(...)`, `ProjectileRequestFor`, `AoeRequestFor` | managed submission | **changed** to emit events (T004/T006) | external producer path |
| `System/Projectile/ProjectileCollisionSystem.cs`, `System/Aoe/AoeCollisionSystem.cs` | collision jobs | detect + gate + consequences | **changed**: emit typed events (T004/T006); lifetime/damage renames (T001/T008) | §7.2 final producer |
| `System/Projectile/ProjectileContactGateSystem.cs`, `System/Aoe/AoeContactGateSystem.cs` | gate maintenance | tick + compact gates | **kept** | already matches §7.1 |
| `System/Common/CombatTargetSync*.cs`, `ICombatTarget.cs`, registry/set | target bridge | sync targets, replay | **kept** (refine only) | §8 "do not rebuild" |

## Context Files
- [context/001-current-architecture.md](context/001-current-architecture.md) — what exists today; relied on by all tasks (especially 002–008).
- [context/002-target-architecture.md](context/002-target-architecture.md) — decisive end-state, type shapes, ownership; relied on by 001–009.
- [context/003-data-flow.md](context/003-data-flow.md) — step-by-step final flows + preserved/changed markers; relied on by 003–008, 010.
- [context/004-system-ordering.md](context/004-system-ordering.md) — §9 order + invariants + read/write table; relied on by 001, 003, 004, 005, 006, 009.
- [context/005-decision-log.md](context/005-decision-log.md) — settled decisions; relied on by all tasks.
- [context/006-validation-strategy.md](context/006-validation-strategy.md) — compile/test/profile strategy; relied on by every task's Validation section and by 010.

## Refactor Phases
**Phase 1 — Lifetime unification (Task 001).** Goal: one generic lifetime system. Independent; done first because it is self-contained and removes lifetime components from the archetypes the spawn refactor rebuilds. Risk: pulse-as-disabled mapping.

**Phase 2 — Projectile spawn pipeline (Tasks 002, 003, 004).** Goal: event/command split + expansion + typed consequence emission for projectiles. Must follow Phase 1 (archetype lifetime change). 002/003 are additive (build beside old); 004 is the cutover. Risk: cutover correctness (id/velocity/render-Z/order).

**Phase 3 — AoE spawn pipeline (Tasks 005, 006).** Mirror of Phase 2 for AoE; follows Phase 2 because collision (shared) is restructured there. 005 additive; 006 cutover. Risk: pulse lifetime mapping + impact-AoE parity.

**Phase 4 — Dead-code + damage reshape (Tasks 007, 008).** Goal: remove the generic convert job/pending-spawn and align the damage path to §8 + fix the double-clear. Follows Phases 2–3 (both cutovers done). Risk: AoE-test damage-clear ownership.

**Phase 5 — Order + validation (Tasks 009, 010).** Goal: codify §9 order + R5/§6 invariants, then migrate/extend tests. Last. Risk: ordering regressions reintroducing same-tick recursion.

## Task List
| # | Title | Link | Description | Deps | Files touched (expected) | Risk | Behavior |
|---|---|---|---|---|---|---|---|
| 001 | Unify lifetime | [tasks/001](tasks/001-unify-lifetime.md) | `CombatLifetimeComponent` + `CombatLifetimeSystem` + `AoePulseVfxSystem`; delete 2 lifetime systems | — | lifetime systems, both EcsComponents, both spawn+collision systems, AoeSimulationTests | Medium | preserve (1 minor VFX change) |
| 002 | Projectile spawn types | [tasks/002](tasks/002-projectile-spawn-types.md) | add `ProjectileSpawnEvent`/`...CommandData` + build helpers + scope buffer (additive) | 001 | new `ProjectileSpawnPipeline.cs`, `CombatEcsComponents.cs` | Low | preserve |
| 003 | Projectile expansion+apply | [tasks/003](tasks/003-projectile-expansion-apply.md) | new expansion + apply systems beside old (inert) | 002 | 2 new systems | Medium | preserve |
| 004 | Projectile cutover | [tasks/004](tasks/004-projectile-producer-cutover.md) | producers → events; delete old expand/spawn/child systems + dual-use element | 003 | CombatRoot, both collisions, new TimedProjectileSpawnSystem, deletes | High | preserve |
| 005 | AoE spawn types+systems | [tasks/005](tasks/005-aoe-spawn-types-systems.md) | add AoE event/command + expansion + apply beside old (inert) | 001,003 | new `AoeSpawnPipeline.cs`, 2 new systems, `CombatEcsComponents.cs` | Medium | preserve |
| 006 | AoE cutover | [tasks/006](tasks/006-aoe-producer-cutover.md) | AoE producers → events; delete `AoeSpawnSystem` + element; drop convert schedule | 005,004 | CombatRoot, both collisions, deletes, AoeSimulationTests | High | preserve |
| 007 | Remove convert job | [tasks/007](tasks/007-remove-convert-job.md) | delete `CombatSpawnConvertJob` + `CombatPendingSpawn` | 004,006 | `CombatHitElement.cs`, delete convert job | Low | preserve |
| 008 | Damage rename + bridge | [tasks/008](tasks/008-damage-replay-rename-and-bridge.md) | `DamageReplayEvent`/`DamageDispatchBridge`; single damage-clear owner | 004,006 | CombatHitElement, flush job, dispatch system, both sim systems, both collisions | Low–Med | preserve |
| 009 | Codify phase order | [tasks/009](tasks/009-codify-phase-order.md) | §9 order attributes + R5/§6 container audit | 001,004,006,008 | ordering attributes across systems | Low–Med | preserve |
| 010 | Tests + validation | [tasks/010](tasks/010-tests-and-validation.md) | migrate + add tests (fan-out, next-tick, pulse, reuse, parity) | 001–009 | all test files + 1–2 new | Low | preserve |

## Dependency Graph
```
001 ─┬─> 002 ─> 003 ─> 004 ─┬─> 007 ─┐
     │                      │        │
     └─> 005 ──────> 006 ───┴─> 008 ─┤
                       ^              │
            004 ───────┘ (shared collision) 
                                      v
                            009 ─> 010
(004 and 006 both feed 007, 008, 009; 005 needs 001+003; 006 needs 005+004)
```

## Global Invariants
Every implementation task MUST preserve:
1. **Next-tick spawn (R5):** entities created/reused in apply do not move, collide, or emit until the next frame — enforced by system order only; no `SpawnedThisTick` marker.
2. **No spawn math in apply (R4/§5.4):** `...SpawnCommandData` carries no `Count`/`SpreadDegrees`/`JitterDegrees`/`JitterSeed`/`BaseDirection`/`Speed`. All fan-out/direction/jitter/bounds/render-Z/id resolution happens in expansion.
3. **No structural changes inside jobs:** reuse toggles enableable tags; cold-create uses one ECB playback per apply system per frame.
4. **Container phase discipline (§6):** no system reads a queue still being written; expansion drains queue+buffer to a frozen array and clears both; command output is read once then disposed; owners create/dispose their own native handles (no leaks).
5. **Managed boundary (§6/§8.4):** only `DamageDispatchBridge` reads the managed `ICombatTarget`; no simulation/Burst system touches managed companions.
6. **Domain gating:** projectile/AoE systems query a domain tag (`ProjectileTag`/`AoeTag`); common combat components alone never opt an entity into a domain system.
7. **One damage-buffer clear** per frame; the bridge clears `CombatDamageElement` after replay.
8. **No behavior change unless a task explicitly assigns one** (only D-LIFETIME-VFX changes anything observable).

## Non-Goals (must NOT change)
- Projectile movement, tracking, lifetime math, collision math (`ProjectileMovementSystem`, `ProjectileTrackingSystem`, `CombatCollisionMath`).
- Rendering / VFX dispatch (`CombatBatchedRenderSystem`, `CombatVfxDispatchSystem`, VFX request shapes) beyond the documented despawn-VFX area note.
- Authoring / ScriptableObjects, the managed `ProjectileSpawnCommand`/`AoeSpawnCommand` types.
- The target bridge structure: `CombatTargetRegistry`, `CombatTargetSync`, `CombatTargetSyncSystem`, `ICombatTarget`, the `TargetId`+`Faction` damage identity (no proxy entities).
- The per-domain active tags (no generic `Active`), damage condensation, free-list reuse, timed-AoE / AoE-scatter behavior (slots only).

## Decision Log Summary
Full text: [context/005-decision-log.md](context/005-decision-log.md).
- **D-NAMING:** ECS types `...SpawnEvent` / `...SpawnCommandData`; managed `...SpawnCommand` keep their names. Rejected renaming managed API (out-of-scope churn). Tasks 002–006.
- **D1:** damage keeps `TargetId`+`Faction`; no proxy entities / `Entity TargetProxy`. Rejected proxy rebuild (§1/§11 defer it). Task 008.
- **D2:** keep per-domain active tags; defer generic `Active`. Rejected the rename (wide churn, no gain). Task 001.
- **D-EXPANSION-OWNS-MATH:** expansion owns all spawn math; command has no multiplicity. Rejected branching in apply. Tasks 002–006.
- **D-TRANSPORT:** internal producers use `NativeQueue.ParallelWriter`; managed submission uses a scope buffer; expansion finalizes both. Rejected routing managed through the queue. Tasks 002–006.
- **D-SHAPE-BUCKETING:** keep shape-keyed bucketing; don't build a system per theoretical shape. Rejected speculative proliferation (§5.3). Tasks 003/005.
- **D-CONVERT-RELOCATE:** delete `CombatSpawnConvertJob`/`CombatPendingSpawn`; relocate build logic to typed helpers. Tasks 004/006/007.
- **D-LIFETIME-PULSE:** unify lifetime via enableable `CombatLifetimeComponent`; pulse = disabled. Task 001.
- **D-LIFETIME-VFX:** unified despawn VFX area = render scale for both domains (minor AoE change; fallback documented). Task 001.
- **D-DAMAGE-CLEAR:** one `OrderFirst` system clears `CombatDamageElement`. Task 008.
- **D-AOE-EXPANSION-MINIMAL:** AoE expansion is a real-but-1:1 stage now (parity for future scatter). Tasks 005/006.

## Validation Summary
(Full strategy: [context/006-validation-strategy.md](context/006-validation-strategy.md).)
- **Compile:** Unity Editor recompiles `PlayGround.Runtime` with zero errors after every task; Burst compiles all jobs (events/commands are blittable).
- **Existing tests:** `AoeSimulationTests`, `ProjectileTrackingSimulationTests`, `AoePlayModeTests`, `BareMinimumPrototypePlayModeTests`, `CritEditModeTests`, authoring/skill EditMode tests — migrated and kept green.
- **New tests:** expansion fan-out, event/command split (no `Count` on command), next-tick spawn (R5), unified-lifetime pulse/lingering, reuse, impact-consequence parity.
- **Manual:** `Main.unity` Play mode — single/multi/child/impact projectile attacks, impact/pulse/lingering AoEs, mob-vs-player; `DebugOverlay` counters.
- **Profiling:** spawn markers (`Projectile.Spawn`, `Aoe.Spawn`, `.ReuseJob`, Cold/Reuse counters), Structural Changes module (cold-create only), no new per-frame managed allocations; re-check the ~50k-projectile stress target.
- **Regression risks:** same-tick spawn recursion (ordering), lost/duplicated spawns (finalize draining both sources / old systems not deleted), double or missing damage (clear ownership), pulse-AoE mis-behavior (lifetime enable state), container leaks (disposal).

## Open Questions
None block implementation. The one design open item (§12, "which file is the spawn mess") is **resolved**: the dual-use `ProjectileSpawnRequestElement` + `ProjectileMultiExpandSystem` + `CombatSpawnConvertJob` (see context 001 §2.1). The one risky assumption (AoE despawn-VFX area on render scale, D-LIFETIME-VFX) has a documented in-task fallback and does not block any task.

# Refactor Plan Index

ECS Combat Rewrite — spawn / event / damage / lifetime cluster.
Source design (source of truth): `.agent/rewrite/design.md` + `.agent/rewrite/design-points.md`. Grounded against the real code under `Assets/Scripts/System/`.

## Purpose
Replace the accreted spawn/event/damage cluster with a phase-oriented pipeline that has explicit ownership boundaries (design's primary goal: **architecture clarity**).

**Governing priority (this revision):** faithfully realize the direction's architecture. Preserve old *logic*, not old *architecture*. Churn — renames, archetype changes, a target-bridge rebuild — is accepted; temporary behavior breakage during the rewrite is acceptable as long as the end-state matches the direction. "It already works / merging is wide churn / out-of-scope to rename" is **not** a reason to diverge from the design. The previous revision of this plan made several churn-avoiding compromises that diverged from the design; this revision reverses them (see `context/005-decision-log.md` → Governing principle).

Concretely the target architecture:
- splits spawn **intent** (`...SpawnEvent`, multiplicity) from **allocation intent** (`...SpawnCommand`, exactly one entity) with **R3-correct names** — D-NAMING-EVENTCMD;
- makes one **expansion** phase own all spawn math and one **apply system per real shape** — R4 / §5.3 / §5.4;
- replaces the dual-use element + convert job with typed events + native-container discipline — §6 / §7.2;
- **unifies** lifetime over a generic occupancy flag — §4;
- adopts the generic **`Active`** flag — §3;
- adopts per-target **ECS proxy entities + managed companion** and the native-container **damage transport** (`NativeQueue<DamageReplayEvent>` keyed by `Entity`) — §8.

## Two increments
The prior revision's tasks (lifetime unify, typed spawn events, expansion, projectile/AoE cutover, convert-job removal, damage *rename*) are **already implemented in code** (commits up to `010`). That increment realized the spawn/event/lifetime spine but, to limit churn, **kept** per-domain active tags, the `TargetId`+`Faction` damage identity, the `CombatHitFlushJob`/`CombatDamageElement` buffer path, a single bucketed apply system, and the `...CommandData` / managed-`...SpawnCommand` naming.

**This revision (Increment 2)** is the direction-fidelity work that closes that gap. The task list below is Increment 2.

## Planning Assumptions
- The managed authoring types are **renamed** to Event semantics (they carry multiplicity → R3 intent). Their call sites (`Mob/MobProjectileAttack.cs`, `Skills/SkillSpawnTranslator.cs`, `Mob/MobRoot.cs`, `CombatRoot.cs`) are updated. — **In scope** (D-NAMING-EVENTCMD).
- Per-target ECS proxy entities + a managed companion are **built**; collision and damage move onto them. — **In scope** (D-PROXY-ENTITY). The §8-vs-§11 contradiction in the design is resolved in favor of §8 per explicit user direction.
- The generic `Active` flag **replaces** `ProjectileActiveTag`/`AoeActiveTag` (~30 files). — **In scope** (D-ACTIVE-GENERIC).
- Damage uses `NativeQueue<DamageReplayEvent>` → `NativeArray`; `CombatHitFlushJob` + `CombatDamageElement` are **removed**. — **In scope** (D-DAMAGE-TRANSPORT).
- Contact-gate systems already match the design's "gate-state maintenance only" role. — **Confirmed by codebase** (kept).
- Collision already emits separated damage/spawn/vfx streams. — **Confirmed by codebase** (the spawn stream was already typed in Increment 1; this increment changes the damage identity + transport).

## Target Architecture Summary
Decisive end-state (full detail: `context/002-target-architecture.md`).
- **Ownership:** producers create intent only; one expansion system per domain owns all spawn math; **one apply system per real shape** owns allocation/reuse; collision is the final producer of typed consequence events (now writing `Entity`-keyed damage); `DamageDispatchBridge` is the only reader crossing to the managed companion.
- **Naming (R3):** `ProjectileSpawnEvent`/`AoeSpawnEvent` = intent + multiplicity; `ProjectileSpawnCommand`/`AoeSpawnCommand` (ECS, no `Data` suffix) = one resolved entity; the old managed `...SpawnCommand` authoring DTOs are renamed to Events.
- **Occupancy:** generic `Active : IComponentData, IEnableableComponent`; kind = marker components; reuse = `WithDisabled<Active>()`.
- **Damage/target:** GameObject-owned proxy `Entity` + `TargetCompanion`; `DamageReplayEvent { Entity TargetProxy; … }`; `NativeQueue → NativeArray → DamageDispatchBridge`.
- **Ordering:** §9 codified in `context/004-system-ordering.md`; next-tick spawn (R5) by order only; proxy position pushed in `Update()` before simulation, proxy deleted in `LateUpdate()`.

## Removed pathways (Increment 2 adds these removals)
Already removed in Increment 1: dual-use `ProjectileSpawnRequestElement`, `ProjectileMultiExpandSystem`, `ProjectileSpawnSystem`, `ProjectileChildSpawnSystem`, `AoeSpawnSystem`, `CombatSpawnConvertJob`, `CombatPendingSpawn`, `ProjectileLifetimeSystem`, `AoeLifetimeSystem`, the lifetime components, `AoeSpawnRequestElement`.
**Increment 2 removes:** `ProjectileActiveTag` / `AoeActiveTag` (→ `Active`), `CombatHitFlushJob` + `CombatDamageElement` buffer (→ `NativeQueue` transport), the `CombatTargetElement` buffer sync (`CombatTargetSync`/`CombatTargetSyncSystem` buffer-fill → proxy entities), the `...CommandData` type name and the managed `...SpawnCommand` names (→ Event/Command split), the `(TargetId,Faction)` damage identity (→ `Entity`).

## Context Files
- [context/001-current-architecture.md](context/001-current-architecture.md) — what exists today (now: the Increment-1 end-state).
- [context/002-target-architecture.md](context/002-target-architecture.md) — decisive end-state, type shapes, ownership.
- [context/003-data-flow.md](context/003-data-flow.md) — step-by-step final flows + preserved/changed markers.
- [context/004-system-ordering.md](context/004-system-ordering.md) — §9 order + invariants + read/write table.
- [context/005-decision-log.md](context/005-decision-log.md) — settled decisions (incl. the reversals).
- [context/006-validation-strategy.md](context/006-validation-strategy.md) — compile/test/profile strategy.

## Refactor Phases (Increment 2)
**Phase A — Generic `Active` (Task 011).** Foundational, wide-but-mechanical. Replace both active tags with `Active`. Do first because the apply/collision/lifetime reworks all query the occupancy flag. Risk: blast radius (every domain system + test).

**Phase B — Event/Command naming (Task 012).** Rename managed authoring `...SpawnCommand` → Event; ECS `...CommandData` → `...SpawnCommand`. Mostly mechanical but crosses authoring/skill code. Independent of A; can run in parallel.

**Phase C — Per-shape apply (Task 013).** Split the bucketed apply into per-shape command containers + per-shape apply systems. Needs A (queries `Active`) and B (command name).

**Phase D — Proxy target bridge (Task 014).** Proxy entities + companion + GameObject lifecycle; collision reads proxies; retire the `CombatTargetElement` sync. Largest/highest-risk. Independent of A–C in code areas but shares collision with E.

**Phase E — Damage transport (Task 015).** `Entity`-keyed `DamageReplayEvent`; `NativeQueue → NativeArray`; delete `CombatHitFlushJob` + `CombatDamageElement`; bridge resolves `Entity → companion`. Needs D (proxy `Entity` exists).

**Phase F — Order + validation (Tasks 016, 017).** Re-codify §9 (proxy push/delete, damage queue discipline, `Active` queries); migrate/extend tests. Last.

## Task List (Increment 2)
| # | Title | Link | Description | Deps | Risk |
|---|---|---|---|---|---|
| 011 | Generic `Active` | [tasks/011](tasks/011-generic-active.md) | introduce `Active`; replace `ProjectileActiveTag`/`AoeActiveTag` everywhere; reuse queries → `WithDisabled<Active>()` | — | Med (wide) |
| 012 | Event/Command rename | [tasks/012](tasks/012-event-command-rename.md) | managed `...SpawnCommand` → Event; ECS `...CommandData` → `...SpawnCommand`; fix authoring/skill call sites | — | Med |
| 013 | Per-shape apply | [tasks/013](tasks/013-per-shape-apply.md) | split bucketed apply → per-shape command containers + per-shape apply systems (projectile basic/child-spawner, AoE) | 011,012 | Med |
| 014 | Proxy target bridge | [tasks/014](tasks/014-proxy-target-bridge.md) | proxy entity + `TargetCompanion`; GameObject create/push/delete lifecycle; collision reads proxies; retire `CombatTargetElement` sync | 011 | High |
| 015 | Damage transport | [tasks/015](tasks/015-damage-transport.md) | `Entity`-keyed `DamageReplayEvent`; `NativeQueue→NativeArray`; delete `CombatHitFlushJob`+`CombatDamageElement`; bridge resolves `Entity→companion` | 014 | High |
| 016 | Codify phase order | [tasks/016](tasks/016-codify-phase-order.md) | §9 order incl. proxy push (Update)/delete (LateUpdate), damage-queue phase discipline, `Active` queries | 011,013,014,015 | Low–Med |
| 017 | Tests + validation | [tasks/017](tasks/017-tests-and-validation.md) | migrate tests to `Active`/proxy/Event-Command; add proxy-lifecycle, Entity-keyed damage, per-shape reuse tests | 011–016 | Low–Med |

> The Increment-1 task files `tasks/001`–`tasks/010` describe landed work; they remain for history. Where Increment 2 changes their end-state, the change is captured in the 011–017 tasks above and in `context/002`.

## Dependency Graph
```
011 (Active) ─┬─> 013 (per-shape apply) ─┐
              ├─> 014 (proxy bridge) ─> 015 (damage transport) ─┤
012 (rename) ─┘            (shared collision)                   ├─> 016 ─> 017
                                                                │
        013, 014, 015 ──────────────────────────────────────────┘
```

## Global Invariants
Every implementation task MUST preserve:
1. **Next-tick spawn (R5):** entities created/reused in apply do not move/collide/emit until the next frame — system order only; no marker.
2. **No spawn math in apply (R4/§5.4):** `...SpawnCommand` carries no `Count`/`Spread`/`Jitter`/`BaseDirection`/`Speed`. All resolution happens in expansion. Apply contains no shape-key branch (D-SHAPE-EXPLICIT).
3. **No structural changes inside jobs:** reuse toggles `Active`; cold-create uses one ECB playback per apply system per frame.
4. **Container phase discipline (§6):** no system reads a queue still being written; expansion drains queue+buffer to a frozen array and clears both; the damage queue is finalized to an array, read once, then cleared; owners create/dispose their own native handles.
5. **Managed boundary (§8.4):** only `DamageDispatchBridge` reads the `TargetCompanion`; no simulation/Burst system touches managed companions.
6. **Domain gating:** projectile/AoE systems query a domain marker (`ProjectileTag`/`AoeTag`); the generic `Active` alone never opts an entity into a domain system.
7. **Proxy lifecycle (§8.3):** the GameObject pushes proxy state in `Update()` before simulation and deletes the proxy in `LateUpdate()`; no unresolved damage event references a deleted proxy.
8. **No behavior change beyond the documented ones** (D-LIFETIME-VFX area; the `Entity`-keyed identity is a representation change, not a gameplay change).

## Non-Goals (must NOT change)
- Projectile movement, tracking, lifetime math, collision math (`ProjectileMovementSystem`, `ProjectileTrackingSystem`, `CombatCollisionMath`).
- Rendering / VFX dispatch (`CombatBatchedRenderSystem`, `CombatVfxDispatchSystem`, VFX request shapes) beyond the documented despawn-VFX area note.
- Authoring / ScriptableObject **definitions** (the *managed authoring DTO types* are renamed per D-NAMING-EVENTCMD, but the ScriptableObject schema/data is untouched).
- Damage condensation (§12), timed-AoE / AoE-scatter behavior (slots only — D-AOE-EXPANSION-MINIMAL), VFX/audio consequence streams (§12), ECS-owned target health / full hybrid model (§12).
- The per-feature opt-in tags (`CombatRenderActiveTag`, collision-active) — only the *occupancy* flag is unified into `Active`.

## Decision Log Summary
Full text: [context/005-decision-log.md](context/005-decision-log.md).
- **Governing principle:** architecture fidelity over churn-avoidance; the listed prior compromises are reversed.
- **D-NAMING-EVENTCMD:** managed authoring `...SpawnCommand` (multiplicity) → Event; ECS one-entity type → `...SpawnCommand` (no `Data`). Tasks 012, 013.
- **D-PROXY-ENTITY:** proxy entities + companion; `DamageReplayEvent` keyed by `Entity`. §8 wins over §11. Tasks 014, 015.
- **D-ACTIVE-GENERIC:** generic `Active` replaces per-domain tags. Task 011.
- **D-DAMAGE-TRANSPORT:** `NativeQueue → NativeArray`; remove `CombatHitFlushJob` + `CombatDamageElement`. Task 015.
- **D-SHAPE-EXPLICIT:** one apply system per real shape; no bucketed apply, no speculative shapes. Task 013.
- Carried over: D-EXPANSION-OWNS-MATH, D-TRANSPORT, D-CONVERT-RELOCATE, D-LIFETIME-PULSE, D-LIFETIME-VFX, D-AOE-EXPANSION-MINIMAL.

## Open Questions
None block implementation. Resolved items:
- **§8 vs §11 contradiction** — §8 (proxy entities + companion) is authoritative per user direction; §11's "don't rebuild the target bridge" was the churn-avoidance reading and is overridden.
- **Damage container** — decided: `NativeQueue<DamageReplayEvent>.ParallelWriter → NativeArray` (§8.1), not NativeStream.
- **Managed authoring names** — decided: `ProjectileSpawnRequest` / `AoeSpawnRequest`.

All plan files are written decisively (exact type names, container types, file locations, lifecycle timing). An implementation agent should not need to choose; if a detail is genuinely missing, treat it as a plan bug and flag it rather than guess.

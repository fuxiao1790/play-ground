---
name: targeted-skills-plan
description: Implementation plan for targeted (no-physics) chain skills — single-hit and interval variants
---

# Targeted Skills — Implementation Plan

Requirements: [requirements.md](./requirements.md). All twelve specification decisions are locked
there (§13.1); this plan implements them and does not reopen them.

---

## 1. Summary

Add a third combat domain, **Targeted**, beside `Projectile` and `Aoe`. A targeted skill resolves
victims by broadphase query instead of simulated geometry: it picks a first target near the
acquisition anchor, then walks link-to-link outward, emitting one hit and one `LineSegment` VFX per
link, optionally staggered by a per-link delay.

Two variants, mirroring impact vs lingering AOE at every layer:

| | Single hit | Interval tick |
|---|---|---|
| Condition | `lifetimeSeconds == 0` | `lifetimeSeconds > 0` |
| Skill SO | `TargetedSkill` | `LingeringTargetedSkill` |
| Spawn kind | `IntervalChildKind.Targeted` | `IntervalChildKind.LingeringTargeted` |
| Pool tag | *(absent)* | `LingeringTargetedTag` |

15 tasks, bottom-up: ECS contracts → lanes → resolve → integration → authoring → compile → wiring
→ validation → tests → docs. Tasks 1–7 are testable in EditMode without any authored asset.

---

## 2. Rationale For Major Decisions

**Why a new domain rather than a tuned AOE or projectile.** A zero-lifetime AOE hits *everything*
in a circle; a chain hits N nearest, each from the previous one's position. That is a different
selection rule, not a parameter. A homing+pierce projectile approximates the behaviour but pays
movement, collision, sweep, and render cost per link, and its timing is coupled to travel speed.

**Why a real entity for something instantaneous.** `CombatApplyFinalizeSingleSystem` resolves
damage through `ComponentLookup<CombatHitPayload>[hit.Source]`
([CombatApplyFinalizeSingleSystem.cs:221](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L221)).
An entity carrying `CombatHitPayload` is a precondition of the hit contract, even for a single-hit
chain. That is also what lets the feature reuse pooling, lifetime, arming, and the spawn pipeline
wholesale rather than inventing a parallel path.

**Why `CombatHitEvent` gains `DamageScale`.** One source entity emits N links at N damages, while
`CombatHitPayload` is one component per entity. Alternatives rejected: one entity per link (a
6-jump chain becomes 6 pooled entities per cast); mutating the payload per link (a data race under
`ScheduleParallel`). It is a damage field on a damage contract, so it does not violate the
standards rule against widening damage events with spawn-routing data.

**Why the exclusion state is one `int`.** The degenerate case the exclusion exists to prevent is a
link selecting its own source at distance zero. Only the immediately previous target must be
excluded for that. One `int` also collapses the interval variant's last-target carry-over into the
same field, and removes overflow as a concept.

**Why no liveness check.** Damage aggregates in finalize, which runs after every resolve, so
mid-frame `Health` is stale by construction (ADR-006). A `ComponentLookup<Health>` would cost
random access per candidate and still not prevent overkill.

---

## 3. Constraints And Invariants

Each must hold after the change; source in brackets.

| # | Invariant | Source |
|---|---|---|
| C1 | Domain systems must require a domain tag. `Active`, shared components, and scope membership never imply domain or faction. | `Docs/reference/simulation/index.md` change rules; `Docs/coding-standards.md` hybrid rule |
| C2 | Simulation jobs must not read GameObjects, Transforms, Colliders, live SOs, or managed `TargetCompanion`. | `Docs/layers/ecs-simulation.md` forbidden dependencies |
| C3 | Events are gameplay intent (registry link + instance frame); commands are allocation intent (one per entity). Expansion owns the registry dereference and the multiplicity explosion. | `Docs/contracts/spawn-events-and-commands.md` |
| C4 | The spawn-template registry is written only by managed pre-tick code and is read-only during the simulation tick. | same |
| C5 | Hot despawn is enable/disable, never destroy. Apply reuses disabled slots before cold creation. | ADR-005; `ecs-notes.md` pool cleanup |
| C6 | Systems must not reach into other systems' fields. Cross-system data goes through singleton lanes with explicit `ProducerHandle`/`PendingHandle`. | `Docs/coding-standards.md` System Encapsulation |
| C7 | Lane singletons are read directly and must throw when missing — a missing lane is a broken world, not a skip condition. | [StatusProcessSystem.cs:59-66](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L59); project memory |
| C8 | Spatial-hash consumers combine `BuildHandle` into their dependency and publish into `ConsumerHandle`. | [TargetSpatialHashSystem.cs:88-99](../../Assets/Scripts/System/Api/Collision/Broadphase/TargetSpatialHashSystem.cs#L88) |
| C9 | Combat hot paths are allocation-light: no managed allocation, no LINQ, no closure capture, no growing native container per entity. | `Docs/coding-standards.md` Allocation Rule |
| C10 | Apply runs after movement and collision; newly spawned entities do not act until the next simulation update. | `Docs/architecture/phase-order.md` |
| C11 | Every scalable combat system needs an obvious budget and a fallback path, with debug counters before content stress tests. | `Docs/coding-standards.md` Performance Budget Rule |
| C12 | Component/tag/buffer declarations carry `ECS Lifecycle:` comments, updated in the same change that alters the lifecycle. | `Docs/coding-standards.md` |
| C13 | Native/ECS/GPU handles are disposed by their owner on every teardown path. | `Docs/coding-standards.md` |
| C14 | Unity editor work — prefabs, SO assets, VFX graphs, atlas entries, loadout wiring — is performed by the user, never by hand-editing YAML. | project memory |
| C15 | Numeric stats resolve through the shared `StatFold`; no new per-stat fold. | `Docs/reference/architecture/numeric-modifiers.md` |

---

## 4. Mechanisms Reused vs Introduced

**Reused as-is — no new code:**

- Broadphase: `TargetSpatialHashSingleton.AoeOccupiedCells` + the parallel snapshot arrays — the
  same bounds-expanded hash AOE area queries use, rebuilt every frame. **No new spatial
  structure.** Narrow phase reuses `CombatCollisionMath`, so a chain decides "is this target in
  range" with the same test an AOE does.
- Hit path: `CombatHitEvent` → `CombatHitDispatchSingleton` → `CombatApplyFinalizeSingleSystem` →
  `CombatTickResult` → `CombatApplyBridge`. No targeted-specific presentation code.
- Pooling: `Active` enableable + `SpawnPoolTopUp.EnsureDisabledSlots` + `CombatPoolCleanupSystem`.
- Arming: `ArmingTag` + `CombatArmingComponent` pause overlay.
- Lifetime: `CombatLifetimeComponent` + `CombatLifetimeSystem`.
- Render: `CombatRenderComponent.AlignToVelocity` + `CombatRenderMatrixUtility.ElementFor`. The
  "point along a direction" mechanism projectiles already use.
- VFX: the `LineSegment` lane — request struct, `VfxEmit.EnqueueLineSegment`, bucketing job, GPU
  buffers, dispatch — is fully built and currently has **no gameplay producer**.
- Mana gate: `ExternalSpawnRequest` → `ExternalSpawnGateSystem` → `SpawnRejectedSingleton` →
  `SkillDriver` cooldown refund.
- Stat folding: `StatModifierAccumulator` + `StatFold`, with radii mapped onto the existing
  `AreaSize` stat.
- Interval triggers: `IntervalSpawnTrigger` energy accrual, `TimedSpawnComponent`, `TimedSpawnSystem`.
  A targeted child costs one enum value and one routing branch — no new component.

**Introduced, with justification:**

| New thing | Why it cannot reuse | Task |
|---|---|---|
| `TargetedTag`, `LingeringTargetedTag` | C1 requires a domain tag; the variant tag mirrors `LingeringAoeTag`. | 002 |
| `TargetedChainComponent` (walk state) | No existing component holds link index, two link endpoints, an origin, an anchor, and a last-target key. | 002 |
| `TargetedSpawnEvent` / `LingeringTargetedSpawnEvent` / `TargetedSpawnCommand` | Structural warning — see §6. Decision 7 defers the collapse. | 002 |
| Two expansion + two apply systems | Mirrors `ImpactAoe*`/`LingeringAoe*`; sharing a core, not the systems. | 003 |
| `TargetedResolveCore` + two resolve systems | Mirrors `AoeCollisionCore` + the two AOE collision systems. | 004 |
| `CombatHitEvent.DamageScale` | No per-hit damage channel exists. §2. | 001 |
| `TargetedTypeDefinition`, `TargetedTypeRegistry` | Sim-side registration type mirroring `AoeTypeDefinition` / `AoeTypeRegistry`; minus the collision shape. | 007 |
| `TargetedPrefab`, two skill SOs, two definitions | Skills-side authoring mirroring `BasicAoePrefab` / `AoeSkill` / `LingeringAoeSkill`. | 008 |
| `OnImpactTargetedTrigger`, `TargetedIntervalSpawnTrigger` | Trigger links are typed by output domain; a third domain needs its two. | 010 |
| `MultipleChainsSupport` | Third member of the `MultipleProjectiles`/`MultipleAoes` family. | 011 |

---

## 5. Design Validation

| Invariant | How the design holds |
|---|---|
| C1 | Every targeted query includes `TargetedTag`; the variant systems additionally filter on `LingeringTargetedTag` presence/absence, exactly as the AOE pools do. |
| C2 | The resolve job reads only the broadphase snapshot arrays (positions, shapes, factions, entities) and its own chain state. **No `ComponentLookup` at all** — the liveness check that would have needed one was dropped (§2). |
| C3 | Producers enqueue an event carrying template key + origin + anchor + faction + ids. `TargetedExpansionCore` dereferences the template and emits one `TargetedSpawnCommand` per fork. |
| C4 | Templates are registered from `CombatRoot` during compile (managed, pre-tick) and read `[ReadOnly]` in the expansion job. |
| C5 | Apply mirrors `ImpactAoeSpawnApplySystem`: `SpawnPoolTopUp.EnsureDisabledSlots` then a chunk job over `WithDisabled<Active>`. Expiry disables, never destroys. |
| C6 | `TargetedSpawnEventSingleton` / `LingeringTargetedSpawnEventSingleton` hold queue + commands + `ProducerHandle` + `PendingHandle`. Resolve writes hit and VFX lanes via `AsParallelWriter` and combines handles on the main thread. |
| C7 | Resolve reads its lanes with `GetSingletonRW`, no `TryGet` guard. |
| C8 | Resolve combines `BuildHandle`, publishes into `ConsumerHandle` — same shape as `LingeringAoeCollisionSystem.OnUpdate`. |
| C9 | Walk state is four `float2` + two `int` + one `float`, all inline. Exclusion is one `int`. No per-entity container, nothing to allocate or grow. |
| C10 | Resolve runs before apply, so a chain spawned this frame first resolves next frame. Documented consequence: triggered chains land one frame after their cause, identical to impact AOEs. |
| C11 | `MaxChainTargets` and `MinTickInterval` bound authored counts; search needs no radius cap because the occupied-cells hash makes query cost track targets present, not radius. Spawn/despawn counts feed `CombatStatsSingleton` so the pool-cleanup calm-down gate stays correct. Scene-level concurrency is deferred (§8). |
| C12 | Task 002 carries the lifecycle comments; every later task that changes a lifecycle updates them in the same commit. |
| C13 | Each lane singleton disposes its queue and command list in the owning system's `OnDestroy`, mirroring `ImpactAoeSpawnExpansionSystem.OnDestroy`. |
| C14 | Task 008 and task 015 list editor work as **user steps** with explicit instructions; no asset YAML is authored by the agent. |
| C15 | `acquireRadius`/`chainRadius` fold through the existing `AreaSize` stat; `Damage`, `ManaCost`, `Rate` unchanged. |

---

## 6. Additive vs Refactor Comparison

The structural warning in this change is real and was surfaced during specification.
`ProjectileSpawnEvent`, `ImpactAoeSpawnEvent`, and `LingeringAoeSpawnEvent` are already
field-for-field identical, each with its own singleton lane and expansion system. This feature adds
**two more**, taking the count from three to five.

**Minimal/additive approach (chosen)**
- Resulting data flow: five parallel event lanes, five expansion systems, five apply systems, all
  structurally identical, discriminated by static type.
- New concepts/types introduced: `TargetedSpawnEvent`, `LingeringTargetedSpawnEvent`, two lane
  singletons, two expansion systems, two apply systems.
- Copies/translations added: none beyond the existing per-lane pattern — each lane still does
  event → command with no extra hop.
- Long-term cost: every future producer must be wired into the correct one of five lanes; a sixth
  domain repeats the whole shape again; a change to the event frame (adding a field like the
  acquisition anchor) must be replicated five times.

**Refactor approach (rejected for now)**
- Resulting data flow: one `CombatSpawnEvent` discriminated by the `IntervalChildKind` the struct
  *already carries*, one lane, one expansion system dispatching to per-domain expansion cores.
- Existing concepts/types changed or removed: three event structs and three lane singletons
  collapse to one; three expansion systems collapse to one with a switch.
- Copies/translations removed or avoided: the per-lane duplication of drain, scope-buffer gather,
  dispose, and handle plumbing — roughly 250 near-identical lines today.
- Long-term benefit: adding a domain becomes an enum value plus an expansion core, not a lane.

**Decision: additive.** Not by default — by explicit user decision (requirements §13.1, decision 7).
The refactor touches every spawn producer in the codebase and every expansion system, which is a
wider blast radius than the feature that surfaced it. Bundling them would make both harder to
review and would put a working combat pipeline at risk for a cleanup.

**Debt recorded**, not dropped: a `refactor debt` entry now heads `Docs/todo.md` naming the five
structs and the target shape. The right sequencing is to land targeted skills, then do the collapse
as its own task while five identical copies make the pattern maximally obvious.

**Second comparison — variant split.** Splitting single-hit from interval into two archetypes and
two pools (decision 9) is itself the more-additive option: one archetype with a nullable tick gate
would be fewer types. It was chosen because it mirrors impact/lingering AOE exactly, and because
the interval variant genuinely carries components the single-hit one does not (tick gate,
last-target carry-over). Cost is honest and stated: `CombatPoolCleanupSystem` goes from four pools
to six.

**Default decision rule.** Where two representations describe the same domain concept, refactor
toward one source of truth unless there is a concrete migration reason not to. Applied here: the
spawn-event duplication *is* such a case, and the concrete reason is blast radius plus an explicit
user decision — recorded, not silently taken.

---

## 7. Task List

Bottom-up. Tasks 1–7 need no authored asset and are verifiable in EditMode.

| # | Task | Depends on |
|---|---|---|
| [001](./001-damage-scale-hit-contract.md) | `CombatHitEvent.DamageScale` + finalize applies it | — |
| [002](./002-targeted-components-and-contracts.md) | Domain tags, walk state, spawn event/command/registry types | — |
| [003](./003-spawn-lanes-expansion-apply.md) | Two expansion systems, two apply systems, two pools | 002 |
| [004](./004-resolve-core-and-systems.md) | `TargetedResolveCore` + two resolve systems | 002, 003 |
| [005](./005-lifetime-arming-pool-cleanup.md) | Lifetime jobs, arming, pool cleanup, stats counters | 003 |
| [006](./006-render-and-link-vfx.md) | Render mirror, `renderQuery`, `LineSegment` emission | 004 |
| [007](./007-combat-root-registration-and-spawn-api.md) | `TargetedTypeDefinition` + `CombatRoot` registration + spawn API + gate routing | 002, 003 |
| [008](./008-authoring-prefab-and-skill-types.md) | `TargetedPrefab`, two skill SOs, two definitions | — |
| [009](./009-compiler-runtime-definitions.md) | `RuntimeTargetedDefinition`, compile, template registration | 007, 008 |
| [010](./010-trigger-links.md) | `OnImpactTargetedTrigger`, `TargetedIntervalSpawnTrigger` | 009 |
| [011](./011-supports-and-tag-widening.md) | `MultipleChainsSupport`, `Any` widening, AOE-support tags | 009 |
| [012](./012-root-cast-wiring.md) | `SkillSpawnTranslator` + `SkillDriver` root cast | 009 |
| [013](./013-validation-warnings.md) | All §11 validation cases | 009, 010, 011 |
| [014](./014-playmode-integration-tests.md) | PlayMode integration tests | 012, 013 |
| [015](./015-docs-and-authored-content.md) | Doc updates + user editor steps | all |

Each implementation task carries its own EditMode tests in its acceptance criteria; 014 is
integration only.

---

## 8. Open Questions And Considerations

Two items were raised during specification and are **explicitly deferred** — not blockers, not
open questions. Recorded so they are not rediscovered as surprises:

- *Scene-level concurrency budget.* Per-resolve caps bound one instance; nothing bounds how many
  walk at once, and `count` on an interval-triggered chain is the sharpest multiplier available.
  Deferred. If it ever bites, the fix is a scene-level concurrent-instance cap — additive, and it
  changes no contract in this plan.
- *Rank-offset fork differentiation.* Fork `i` opening on the `i`-th nearest target is the derived
  consequence of "no scatter". Settled as spec'd; task 004 implements it without further question.

The items below are live and affect implementation.

**`IntervalChildKind` switch audit is a correctness risk, not a design one.** Two new enum values
must be handled at `ExternalSpawnGateSystem.AppendInternalSpawn`, `TimedSpawnSystem`,
`AoeCollisionCore`, `ProjectileDiscreteCollisionSystem`, and `CombatRoot`. A missed site fails
silently as a spawn that never happens. Task 002 enumerates them explicitly.

**Interval-trigger traps have bitten this project before.** Two known ones apply unchanged to
targeted children and are covered by task 013: an energy threshold that exceeds what the source can
accrue over its lifetime means the child never spawns; and `tickInterval > lifetime` means a
"ticking" chain fires exactly once. Both are silent without the warnings.

**Editor work is the user's.** Task 008 delivers the C# types; the `TargetedPrefab` prefab asset,
skill SO assets, the `LineSegment` VFX graph, the sprite atlas entry for any debug sprite, and the
loadout wiring are user steps with written instructions (C14).

# AOE Impact / Lingering Clean Separation

## Summary
Impact AOE and lingering AOE are the same domain concept split by one discriminator:
`CombatLifetimeComponent` presence (impact = absent, lingering = present). Today the split
is expressed **three inconsistent ways** — the todo complaint "some combined, some separate":

| Stage | Today | Problem |
|---|---|---|
| Expansion (`AoeSpawnExpansionSystem`) | **Combined** — one `SystemBase` + one `IJob`, buckets into `ImpactCommands`/`LingeringCommands` by `command.Lifetime > 0f` | Only stage that is combined |
| Collision (`ImpactAoeCollisionSystem`, `LingeringAoeCollisionSystem`) | **Separate**, share `AoeCollisionCore` | ~55 lines of scheduling boilerplate duplicated verbatim |
| Apply (`ImpactAoeSpawnApplySystem`, `LingeringAoeSpawnApplySystem`) | **Separate**, share `AoeSpawnApplyUtility` | Large `OnUpdate` scaffolding duplicated verbatim |

**Chosen direction (user):** producers emit variant-typed events — `ImpactAoeSpawnEvent`
and `LingeringAoeSpawnEvent` — and each variant gets its own queue → scope buffer →
expansion system → command list → apply → collision lane. This is the **exact same
pattern** already used to separate `ProjectileSpawnEvent` from `AoeSpawnEvent`
(distinct event struct, distinct scope buffer, distinct `*SpawnExpansionSystem`, chosen at
the producer by an `IntervalChildKind` switch). We extend that established mechanism; we do
**not** invent a new one.

### Reference pattern (what we mirror)
- `ProjectileSpawnEvent` / `AoeSpawnEvent` — one `IBufferElementData` struct per lane
  ([AoeSpawnPipeline.cs:9](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs#L9)).
- `ProjectileSpawnExpansionSystem` / `AoeSpawnExpansionSystem` — each owns a persistent
  `NativeQueue<TEvent> EventQueue`, a `_scopeQuery` over its own buffer type, a
  `NativeList<TCommand>` output, `PendingHandle`, `ProducerHandle`
  ([ProjectileSpawnExpansionSystem.cs:21-117](../../Assets/Scripts/System/Projectile/ProjectileSpawnExpansionSystem.cs#L21)).
- Producers pick the lane by switching on `Kind`:
  - `AoeCollisionCore.EmitHit`: `OnHitSpawn.Kind == Projectile` → projectile writer, `== Aoe` → aoe writer.
  - `ProjectileCollisionSystem`, `TimedSpawnSystem` (`spawn.ChildKind`), `StatusProcessSystem.BuildDetonationSpawn` (`snapshot.Kind`, a `StackDetonationKind`), and two main-thread `CombatRoot` scope-buffer appends.

### Target shape
```
IntervalChildKind { Projectile, ImpactAoe, LingeringAoe }   (Aoe split in two)
StackDetonationKind { None, ImpactAoe, LingeringAoe, Projectile }  (Aoe split in two)

Producers switch on Kind ─► ImpactAoeSpawnEvent  ─► ImpactAoeSpawnExpansionSystem  ─► ImpactCommands  ─► ImpactAoeSpawnApplySystem  ─► ImpactAoeCollisionSystem
                        └─► LingeringAoeSpawnEvent ─► LingeringAoeSpawnExpansionSystem ─► LingeringCommands ─► LingeringAoeSpawnApplySystem ─► LingeringAoeCollisionSystem
```
Every stage is the same shape: **a thin per-variant system over a shared static core**
(`AoeExpansionCore`, `AoeCollisionCore`, `AoeSpawnApplyUtility`).

## Constraints & invariants the change must respect
- **Variant is static per `TemplateKey`, known at authoring.** `AoeExpansionJob` reads the
  template command and never mutates `Lifetime`
  ([AoeSpawnExpansionSystem.cs:171-211](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L171));
  supports are folded into the template at registration and distinct combos mint distinct
  keys (memory: support-modifier-kinds, stacking-support). Every site that authors a
  spawn-ref/snapshot (`OnHitSpawnRef`, `TimedSpawnComponent`, `DetonationSnapshot`, the
  main-thread `AoeSpawnRequest`) has the child's `Lifetime` in hand, so it can pick
  `ImpactAoe` vs `LingeringAoe` at authoring time with **zero runtime lookup**. This is the
  load-bearing enabler for producer-side routing.
- **Two distinct event structs are required, not one reused type.** A scope entity cannot
  hold two `DynamicBuffer<T>` of the same element type; the impact and lingering main-thread
  spawn paths each need their own scope buffer → `ImpactAoeSpawnEvent` and
  `LingeringAoeSpawnEvent` must be distinct `IBufferElementData` types (mirrors
  Projectile/Aoe). Their field layout is identical to today's `AoeSpawnEvent`.
- **2-archetype design is load-bearing** (memory: unify-combat-archetypes). Impact stays
  `CombatLifetimeComponent`-absent. Collision stays two systems (one query can't span
  present+absent lifetime); apply stays two systems (two reuse pools). This change does not
  touch archetypes, collision math, pooling, or VFX.
- **Job-safety / producer-handle protocol** (memory: shared-queue-producer-chaining): each
  expansion system owns its queue; IJobEntity producers auto-chain on it, and the plain
  `IJob` expansion consumes the drained events after `ProducerHandle.Complete()` — identical
  to today's `AoeSpawnExpansionSystem`/`ProjectileSpawnExpansionSystem` lifecycle. Nothing
  new is invented here; the new systems are structural copies.
- **Allocation rules.** Per-frame events array + per-lane command list are `TempJob`,
  completed/disposed at the top of the next update, matching the reference systems.

## Mechanisms reused vs. introduced
- **Reused:** the Projectile-vs-Aoe event/queue/scope-buffer/expansion-system split; the
  `IntervalChildKind`/`StackDetonationKind` producer switch; the "thin per-variant system
  over shared static core" shape already used by collision and apply.
- **Introduced:** two event structs (`ImpactAoeSpawnEvent`, `LingeringAoeSpawnEvent`),
  two expansion systems, `AoeExpansionCore` (shared echo/scatter/stamp/VFX so the two
  expansion jobs don't duplicate). No new discriminator concept, no new queue mechanism.

## Design validation (against each invariant)
- *Static variant known at authoring:* every ref/snapshot builder has `Lifetime` → picks the
  kind; Burst producers route by the ref's already-carried `Kind` with no lookup. PASS.
- *Two-struct necessity:* distinct scope-buffer element types satisfied. PASS.
- *2-archetype / lean impact:* archetypes untouched; collision + apply stay two systems. PASS.
- *Job safety / lifecycle:* new expansion systems are structural clones of the reference
  systems; ownership + handle chaining unchanged. PASS.

## Minimal/additive vs. refactor comparison
- **Additive (keep `AoeSpawnEvent`, filter downstream — the rejected approach):**
  - data flow: one queue → one combined expansion classifying by `Lifetime`.
  - long-term cost: expansion stays the odd-one-out; producers, events, and expansion don't
    match the Projectile/Aoe pattern, so "some combined, some separate" persists.
- **Refactor (this plan — producer-side variant events, mirrors Projectile/Aoe):**
  - data flow: producers classify by `Kind` → two symmetric lanes end-to-end.
  - existing types changed/removed: `AoeSpawnEvent` split into two; `AoeSpawnExpansionSystem`
    split into two; `IntervalChildKind`/`StackDetonationKind` gain the impact/lingering cases.
  - copies/translations removed: no downstream re-classification; the discriminator is set
    once at authoring and carried, exactly like Projectile/Aoe.
  - long-term benefit: one uniform mental model across projectile + both AOE lanes; adding a
    future variant is a mechanical lane copy.
- **Decision:** **refactor** (the user's explicit direction). It conforms the AOE split to the
  pre-existing Projectile/Aoe separation instead of leaving a bespoke combined stage.

## Default decision rule applied
The discriminator (child `Lifetime` → variant) has one source of truth, set at authoring and
carried on the ref/event `Kind`. No duplicate classification path.

## Cost / honesty notes
- Broad but mechanical surface: two `Kind` enums gain cases; ~6 producer sites and ~5
  authoring sites switch on them; tests and docs that name `AoeSpawnEvent` /
  `AoeSpawnExpansionSystem` update.
- The two expansion jobs still enqueue into the one shared VFX queue, so they serialize via
  `ProducerHandle` — this refactor buys **structural clarity, not throughput** (true
  expansion parallelism is a separate task, related to the todo's shared-component item).

## Task list
1. [001-variant-discriminator-and-events.md](001-variant-discriminator-and-events.md) — extend `IntervalChildKind` + `StackDetonationKind`; add `ImpactAoeSpawnEvent`/`LingeringAoeSpawnEvent`; register both scope buffers; add an `AoeVariantFor(lifetime)` helper.
2. [002-classify-at-authoring.md](002-classify-at-authoring.md) — set the right `Kind` at every ref/snapshot/request builder (`PlayerSkillDriver`, `SkillIntervalTemplateBuilder`, `CombatRoot`, `SkillSpawnTranslator`).
3. [003-route-producers.md](003-route-producers.md) — producers switch on `Kind` into the two queues/buffers (`AoeCollisionCore`, both collision systems, `ProjectileCollisionSystem`, `TimedSpawnSystem`, `StatusProcessSystem` + `CombatApplyFinalize*`, `CombatRoot` scope appends).
4. [004-split-expansion-systems.md](004-split-expansion-systems.md) — `AoeExpansionCore` + `ImpactAoeSpawnExpansionSystem` + `LingeringAoeSpawnExpansionSystem` (clone of `ProjectileSpawnExpansionSystem`); retire `AoeSpawnExpansionSystem`; repoint apply systems + all `ProducerHandle`/`EventQueue` fetches + system ordering.
5. [005-dedup-collision-and-apply.md](005-dedup-collision-and-apply.md) — *(optional cleanup)* extract shared scheduling helpers so the already-separate collision + apply systems stop duplicating boilerplate.
6. [006-docs-and-tests.md](006-docs-and-tests.md) — update AOE docs/contracts and tests that name the split types/systems.

## Dependencies
- 001 → 002 → 003 → 004 (sequential; the pipeline must not be half-split at any commit boundary — see each task's "commit safety" note).
- 005 is independent (touches only collision/apply internals).
- 006 last.

## Open questions / audit items
- Find where the AOE scope-event `DynamicBuffer` is registered on the scope entity (the
  `GetBuffer<AoeSpawnEvent>` calls in `CombatRoot` imply it is in the scope archetype); add
  both new buffer types there (task 001).
- `CombatRoot.SpawnRegisteredAoe(templateKey, …)` only has the key. Prefer passing the
  variant from the caller (`SkillSpawnTranslator`, which has the def/lifetime) over a
  `Hash128→variant` side table in `CombatRoot` (task 002).

# Hit Event Source Lookup

Caveman: hit event carry big copy of damage every hit. Waste. Source entity
already hold damage. Make hit event tiny — just source + target. Finalize grab
source entity, jump straight to its damage component, apply. One jump. No copy.
No loop. No query.

## Goal (as specified)

```csharp
public struct CombatHitEvent   // was ~90+B, now 16B
{
    public Entity Source;
    public Entity Target;
}
```

Finalize resolves damage with a single direct index — no copy travelling on the
event, no branch, no loop, no query:

```csharp
CombatHitPayload payload = PayloadLookup[hit.Source];   // ComponentLookup<CombatHitPayload>
```

## Why this needs a source-side change first

The event copy is redundant because the payload already lives on the source
entity — **but not as one indexable component**. Today the identical
`CombatHitPayload` is *nested inside two different components*:

- Projectile: `ProjectileHitComponent.HitPayload` (via the `ProjectileHitPayload` wrapper)
  — [ProjectileEcsComponents.cs:29-34](../../Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs#L29-L34)
- AOE (impact + lingering): `AoeHitSpawnComponent.HitPayload`
  — [AoeEcsComponents.cs:41-45](../../Assets/Scripts/System/Aoes/AoeEcsComponents.cs#L41-L45)

A single `ComponentLookup<T>[Source]` needs `T` to be the **same component on
both archetypes**. It isn't. So the enabling refactor is to **promote the shared
`CombatHitPayload` to a standalone `IComponentData` carried by both projectile
and AOE entities.** This is not a new concept — it is the exact type both already
embed (verified identical: same 6 fields, both even paired with the same
`OnHitSpawnRef`). We are extracting one already-shared struct into one component,
not inventing a parallel one.

## Decision: refactor (collapse the duplicate data path)

Per the plan-changes default rule (two representations of one concept → one
source of truth), refactor wins. This removes representations; it adds none.

### Component model after

- **`CombatHitPayload : IComponentData`** — the damage/crit/stack data, now a
  standalone component on projectile + impact-AOE + lingering-AOE archetypes. The
  single on-entity source of truth. (Reuse the existing type — add the marker
  interface — rather than mint a new `DamagePayload`, to avoid a second type for
  one concept. Optional cosmetic rename to `DamagePayload` if preferred.)
- **`ProjectileHitComponent`** → `{ PierceRemaining, RepeatHitCooldownSeconds, OnHitSpawnRef OnHitSpawn }`
  (nested payload removed; `OnHitSpawn` lifted out of the now-unneeded
  `ProjectileHitPayload` wrapper so collision's on-hit-spawn path still has it).
- **`AoeHitSpawnComponent`** → `{ OnHitSpawnRef OnHitSpawn }` (nested payload removed).
- **`CombatHitEvent`** → `{ Entity Source; Entity Target; }`.

The authoring/config/command layer (`ProjectileSpawnRequest`, `SkillDriver`,
`CombatRoot` templates, spawn commands) keeps carrying `CombatHitPayload` /
`ProjectileHitPayload` as **plain data** — those types are unchanged there. Only
the **ECS entity representation** changes: materialization writes the payload to
the standalone `CombatHitPayload` component instead of nesting it. This bounds
churn to the entity boundary and keeps every authoring call site as-is.

### Finalize

`FinalizeCombatSingleJob` drops the payload fields it read off the event and
gains one read-only `ComponentLookup<CombatHitPayload>`. Per dequeued hit:
`payload = PayloadLookup[hit.Source]`, then the **unchanged** crit-roll /
damage-accrue / stack-accrue logic. No `Kind` field, no `HasComponent` branch —
both archetypes carry the same component, so it is always one direct index. If a
source were somehow missing the component, `HasComponent` guards a skip (defensive
only).

## Constraints & invariants (with source)

1. **Payload valid at finalize time** — the load-bearing invariant. Confirmed from
   ordering: `Collision → CombatApplyFinalizeSingleSystem → SpawnExpansion → SpawnApply(reuse)`.
   - Finalize `[UpdateAfter]` the collision systems + `StatusProcessSystem`,
     `[UpdateBefore]` all `*SpawnExpansionSystem`
     ([CombatApplyFinalizeSingleSystem.cs:43-50](../../Assets/Scripts/System/Application/CombatApplyFinalizeSingleSystem.cs#L43-L50)).
   - Expansion `[UpdateBefore]` the apply systems
     ([ProjectileSpawnExpansionSystem.cs:38-40](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L38-L40)).
   - Pool reuse — the only writer of the payload component (ProjectileSpawnApplySystem,
     AoeSpawnApplySystem) — runs in the apply systems, **after** finalize. A
     projectile that deactivates mid-collision keeps its component data until it is
     reused next frame at earliest. → **Holds from ordering alone; no question needed.**

2. **Job safety** — finalize is a single-threaded `IJob` on `Dependency` after the
   collision jobs complete (`ProducerHandle.Complete()`). A RO `ComponentLookup<CombatHitPayload>`
   cannot alias the collision jobs' writes because finalize depends on them.
   Burst-compatible. Collision jobs read the same component as `in` (RO) — no write
   contention with finalize across the dependency edge.

3. **Same component on both archetypes** — projectile + both AOE kinds carry the
   promoted `CombatHitPayload`, so a single lookup covers every source. Add it to
   every archetype where these entities are created/pooled (CombatRoot archetype
   setup) or materialization's `SetComponentData` will throw.

4. **Gate stays at the producer** — collision still enqueues only when the payload
   deals damage or applies a stack (`DirectDamageEnabled || StackEffect.Enabled`),
   reading its own `in CombatHitPayload`. No no-op hits reach the queue, so finalize
   never wastes a lookup.

## Mechanisms reused vs introduced

- **Reused:** `ComponentLookup` in finalize (already the pattern for `TargetHealth`
  + `TargetStackEntry`); the existing `CombatHitPayload` type (now also a
  component); the unchanged crit/damage/stack accrue math.
- **Introduced:** nothing conceptually new. One marker interface on an existing
  struct; the `ProjectileHitPayload` wrapper becomes unnecessary at the entity
  boundary. `CombatHitKind` is no longer used by the hit lane (leave it defined for
  `CombatHitData`).

## Design validation

| Invariant | Result |
|---|---|
| Payload valid at finalize | ✓ ordering guarantees no reuse before finalize |
| Job safety | ✓ RO lookup; finalize depends on collision completion |
| One component on both archetypes | ✓ promoted `CombatHitPayload` added to all three archetypes |
| Gate behavior unchanged | ✓ gate stays at producer, reads local payload |

## Minimal/additive vs refactor comparison

**Minimal/additive** (keep the copy, just shrink it): payload still copied per hit;
duplicate representation persists; every new payload field must be threaded through
the event; queue bandwidth stays high. Long-term: "one fact, two places" rot.

**Refactor** (this plan): collision writes `{source, target}`; finalize reads the
one on-entity payload component. Removes the per-hit payload copy *and* collapses
the on-entity nesting into one indexable component. New payload fields need zero
event plumbing. ~90B→16B per queued hit in the hot parallel producers.

**Decision: refactor.** One source of truth, one direct jump, fewer copies,
conforms to finalize's existing lookup mechanism.

## Open risk to validate (PlayMode profiling)

Trades **parallel write bandwidth** (big struct copy per hit in the collision jobs,
the hot path at high projectile counts) for **one random-access RO `ComponentLookup`
per hit in the single-threaded finalize job**. When one target is hit by many
distinct sources, those lookups scatter across payload chunks and may cost cache
misses the old travel-with-the-event layout avoided. Expectation: producer-side
savings dominate (net win/wash), but it is load-dependent and the harness cannot run
Unity. Per [[feedback_plan_then_verify_ecs]], **validate with a user-run PlayMode
profiling pass** (busy projectile scene) comparing the `CombatApplyFinalizeSingleSystem`
+ collision markers before/after.

## Tasks

- [001-promote-payload-component.md](001-promote-payload-component.md) — promote `CombatHitPayload` to a shared `IComponentData`; slim `ProjectileHitComponent`/`AoeHitSpawnComponent`; add to archetypes; update the two materialization writers.
- [002-reshape-hit-event.md](002-reshape-hit-event.md) — `CombatHitEvent` → `{ Entity Source; Entity Target; }`.
- [003-producers-set-source.md](003-producers-set-source.md) — collision reads its own `CombatHitPayload` for the gate, enqueues `{Source, Target}`; thread source `entity` + payload through `AoeCollisionCore`/`EmitHit`.
- [004-finalize-source-lookup.md](004-finalize-source-lookup.md) — finalize adds `ComponentLookup<CombatHitPayload>`, reads `PayloadLookup[hit.Source]`, same accrue logic.
- [005-tests-and-doc.md](005-tests-and-doc.md) — update entity-constructing tests + direct `CombatHitEvent` enqueues; refresh the contract doc.

001–004 must land in one compiling commit (the struct/component changes are
breaking). 005 follows immediately.

## Open questions / considerations

- **Component naming:** reuse `CombatHitPayload` as the component (recommended, no
  new type) vs. mint `DamagePayload`. Defaulting to reuse; flag if a rename is wanted.
- **Delete `ProjectileHitPayload` wrapper?** Not required — keep it as an
  authoring-only DTO to bound churn. Its `OnHitSpawn` still flows to
  `ProjectileHitComponent.OnHitSpawn` at materialization. Deleting it is a separate
  cleanup.
- **`SourceNodeId`** loses its hit-lane carrier; verified unused by finalize, still
  reachable via `PayloadLookup[Source].SourceNodeId`. Sole reader is a test harness
  ([005](005-tests-and-doc.md)).

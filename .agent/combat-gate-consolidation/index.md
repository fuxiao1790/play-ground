# Combat Gate Consolidation (pre-refactor)

## Summary

Reduce the enableable "active gate" set on combat entities before the arming
lifecycle (`../common-combat-lifecycle/`) is added, so arming does not pile onto
an already-oversized gate zoo.

Two independent, behavior-identical changes:

1. **Merge the two collision gates** `ProjectileCollisionActiveTag` and
   `AoeCollisionActiveTag` into one generic `CombatCollisionActiveTag`. Every
   collision system already filters by the domain identity tag
   (`ProjectileTag` / `AoeTag`), so a single shared collision gate cannot leak
   across domains. (2 gates -> 1.)

2. **Delete `CombatRenderActiveTag`; derive sprite visibility from `Active`.**
   The render-active bit is written identically to `Active` at every spawn and
   death site today, so `render == Active`. Render systems (prepare, batched
   submit, stats count) select the renderable archetype and use the `Active`
   enabled mask instead of a separate hand-maintained bit. (1 gate removed.)

Net enableable gates on combat entities: **6 -> 4** now (`Active`,
`CombatCollisionActiveTag`, `TimedSpawnComponent`, `ProjectileTrackingComponent`).
When arming later lands it re-introduces exactly one gate (`ArmingTag`), and the
render systems gain a `WithDisabled<ArmingTag>` clause so sprite is suppressed
during arming without resurrecting a render bit. Arming therefore stays net-flat,
not +1.

Both changes are behavior-identical. Verification is the existing PlayMode suite
staying green after the tests' gate symbols are updated. The harness cannot build
or run Unity, so acceptance is user-run PlayMode.

## Rationale

The gate set today conflates two problems:

- The two collision gates are the **same concept split by domain**. Domain is
  already carried by `ProjectileTag` / `AoeTag`, which every collision query
  requires, so the split buys nothing and forces every spawn/death site to know
  two names for one idea.
- `CombatRenderActiveTag` is a **hand-maintained bit that only mirrors `Active`**.
  Its own definition comment says it is "enabled/disabled with the owning domain
  active tag." Every spawn and death site sets it in lockstep with `Active`, so
  it is a second source of truth for "is this slot live" that can only ever
  drift, never diverge usefully — until arming, where the divergence is better
  expressed as `Active && !Arming`.

Collapsing both removes duplicate concepts and duplicate ownership before the
lifecycle work adds a real new phase.

## Constraints & Invariants

- **Pooling uses disabled `Active`.** Spawn apply queries `WithDisabled<Active>()`
  for dead slots; pool cleanup counts enabled `Active`. Neither change touches
  `Active` semantics. Sources:
  [ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs),
  [AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs).

- **Enableable gates are the hot-path chunk-skip mechanism.** The merged collision
  gate stays enableable so collision jobs keep skipping visual-only entities.
  Source: [ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md).

- **Domain identity is the query discriminator, not shared common data.** A shared
  `CombatCollisionActiveTag` is legal only because collision systems still require
  `ProjectileTag` / `AoeTag`. Source:
  [coding-standards.md](../../Docs/coding-standards.md).

- **Render uses stable slots + degenerate instances, not enabled-state filtering.**
  `CombatRenderPrepareSystem` and `CombatBatchedRenderSystem` query with
  `EntityQueryOptions.IgnoreComponentEnabledState` and read the render-active
  enabled *mask* to collapse invisible entities to a degenerate matrix (zero-copy
  slot layout). The render-tag removal must preserve this exactly by reading the
  `Active` mask instead. Sources:
  [CombatRenderPrepareSystem.cs](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs),
  [CombatBatchedRenderSystem.cs](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs).

- **`CombatRenderComponent` is 32 bytes (asserted).** The render tag is a separate
  zero-size enableable component, not a field of the render struct, so removing it
  does not change the asserted size. Source:
  [CombatRenderPrepareSystem.cs](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs).

- **Behavior-identical is the acceptance bar.** Verified: render-active is set to
  the same value as `Active` at every site (projectile spawn = true/true;
  impact AOE spawn = collisionEnabled/collisionEnabled; lingering spawn = true/true;
  every death path disables both). Merged collision gate keeps each site's existing
  enable value; only the type name changes.

## Mechanisms reused vs. introduced

- **Reused:** `Active` (existing occupancy gate) becomes the single source for
  visibility as well as liveness. `ProjectileTag` / `AoeTag` (existing domain
  identity) remain the collision-query discriminator. No new query mechanism.
- **Introduced:** one generic `CombatCollisionActiveTag`, replacing two
  domain-specific tags. Net component-type count drops.

## Design validation

- Pooling: untouched — `Active` queries/counts unchanged. PASS.
- Chunk-skip: merged collision gate stays enableable; collision jobs unchanged in
  shape. PASS.
- Domain isolation: collision systems keep `ProjectileTag` / `AoeTag`; shared gate
  cannot match the wrong domain. PASS.
- Render degenerate-slot behavior: prepare/batched swap render mask -> `Active`
  mask, still under `IgnoreComponentEnabledState`; since render==Active today the
  degenerate set is identical. PASS.
- Struct size assert: render tag is external to `CombatRenderComponent`. PASS.

## Minimal/additive vs. refactor comparison

- **Additive approach** (status quo / do nothing, or add arming on top):
  - resulting data flow: two collision gates + a render gate that mirrors `Active`,
    all hand-maintained at every spawn/death site.
  - new concepts/types introduced: none removed; arming would add a 7th gate.
  - copies/translations added: none, but every new lifecycle site must set 3
    redundant bits.
  - long-term cost: duplicate concepts and duplicate ownership grow with every
    phase added; drift risk at each spawn/death site.

- **Refactor approach** (this plan):
  - resulting data flow: one collision gate, visibility derived from `Active`.
  - existing concepts/types changed or removed: `ProjectileCollisionActiveTag` and
    `AoeCollisionActiveTag` merged; `CombatRenderActiveTag` removed.
  - copies/translations removed: two of the three redundant per-site enable writes
    disappear.
  - long-term benefit: one source of truth for liveness/visibility; arming adds a
    single meaningful gate instead of stacking onto redundancy.

- **Decision:** choose refactor.
- **Reason:** both removed gates are duplicate representations of an existing
  concept (`Active`, and domain-split collision). The default decision rule below
  applies directly.

## Default decision rule

Two representations of the same domain concept collapse to one source of truth
unless there is a concrete compatibility or migration reason not to. There is none
here: no serialized asset references these runtime-only enableable tags, and both
changes are behavior-identical.

## Tasks

Both subtasks are behavior-identical and have **no ordering dependency** on each
other; either can be implemented and PlayMode-tested first. They edit a few shared
spawn/death files (`ProjectileSpawnApplySystem`, `AoeSpawnApplySystem`,
`ProjectileCollisionSystem`, `CombatLifetimeSystem`, `AoeCollisionCore`) in
different, non-overlapping spots, so sequential either-order work is clean; only
simultaneous separate-branch development would need a merge.

- [001-merge-collision-active-tags.md](001-merge-collision-active-tags.md) —
  `ProjectileCollisionActiveTag` + `AoeCollisionActiveTag` -> `CombatCollisionActiveTag`.
- [002-remove-render-active-tag.md](002-remove-render-active-tag.md) —
  delete `CombatRenderActiveTag`; render/stats systems use `Active`.

## Open questions / considerations

- Whether `CombatCollisionActiveTag` should live in `System.Common` (recommended,
  alongside `Active`) or stay per-domain-file. Recommendation: Common, since it is
  now domain-agnostic.
- Follow-on (out of scope here, tracked by `../common-combat-lifecycle/`): arming
  adds `ArmingTag` and the render/movement/collision queries gain
  `WithDisabled<ArmingTag>`; sprite-off-during-arming needs no render bit.

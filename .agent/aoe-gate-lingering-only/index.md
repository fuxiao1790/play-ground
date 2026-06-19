# Plan: Split Impact vs Lingering AOE by Archetype

> **Scope: in-chunk footprint / packing only — NOT an allocation fix.** Splitting
> impact (single-hit / pulse) and lingering AOEs by archetype removes the 256-byte
> gate buffer (and two other lingering-only components) from impact AOEs, shrinking
> their chunk footprint and structural-move copy cost. It does **not** touch the
> per-frame `UnsafeUtility.Malloc` seen under `AoeCollisionJob` — that is `NativeQueue`
> block churn, a separate, independent task:
> [collision-writer-container-churn/index.md](../collision-writer-container-churn/index.md).

## Summary

Today a single AOE archetype ([AoeSpawnApplySystem.OnCreate](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L43))
carries `AoeContactGateElement` on **every** AOE entity. With
`[InternalBufferCapacity(32)]`, that is **256 bytes inlined per entity** plus
`CombatLifetimeComponent` and `AoePulseVfxComponent` — all three meaningful only to
lingering AOEs. We split AOEs into two archetypes at spawn, discriminated by the
**presence** of `CombatLifetimeComponent`:

- **Lingering archetype** (`cmd.Lifetime > 0f`): has `CombatLifetimeComponent`
  (always enabled), `AoeContactGateElement` (cap 32), `AoePulseVfxComponent`.
- **Impact archetype** (`cmd.Lifetime <= 0f`): omits all three. Intra-tick multi-cell
  de-dup is done with a `stackalloc` scratch instead of a retained buffer.

## Rationale (locked decisions)

- **Discriminator = component *presence*, not the enabled bit.** Enableable components
  stay allocated in-chunk regardless of enabled state, so toggling `CombatLifetimeComponent`
  cannot drop the buffer; only a structural archetype difference does. Lifetime presence
  is the natural axis — cross-frame repeat-hit gating is only meaningful for an entity
  that exists across frames.
- **Three components ride the split, not just the buffer (Q2 resolved).**
  `CombatLifetimeComponent`, `AoeContactGateElement`, `AoePulseVfxComponent` are all
  lingering-only. `AoePulseVfxComponent` (periodic lingering visual, interval driven by
  `RepeatHitCooldownSeconds`) is lingering-only despite its name.
- **Impact de-dup = `stackalloc int[MaxAoeTargetsPerTick]` (Q1 settled).** Hit-once is
  preserved by *any* de-dup mechanism, so the choice is purely performance (avoid
  per-entity heap malloc). Stack scratch is heap-free, Burst-friendly, dies with the
  `Execute` call, and the cap already bounds registrations to 32. Not a blocker.
- **Routing uses the discriminator itself — no new tag.** Dead-slot pools split on
  `WithAll<CombatLifetimeComponent>` (lingering) vs `WithNone<CombatLifetimeComponent>`
  (impact).
- **Accepted edge (Q2).** A *visual-only lingering* AOE (lifetime > 0,
  `AoeCollisionActiveTag` disabled) still carries the unused gate buffer. The precise
  buffer axis would be lifetime ∧ collision ∧ repeat-cooldown, but that means more
  archetypes; visual-only lingering AOEs are rare and long-lived, so they don't stress
  cold-create. Take the lifetime proxy over a three-way split.

## Constraints to honor

1. **The collision job's signature requires the buffer.** `AoeCollisionJob` takes
   `DynamicBuffer<AoeContactGateElement>` ([AoeCollisionSystem.cs:169](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L169));
   an archetype without the buffer drops out of that query and would stop colliding.
   The collision step must handle both archetypes — and must land **before** the impact
   archetype is introduced (see execution order).
2. **Impact AOEs have no lifetime system to deactivate them.** With `CombatLifetimeComponent`
   absent, `CombatLifetimeSystem` never touches impact AOEs, so the impact collision
   variant **must unconditionally deactivate** after its pass. (Assumes every impact
   AOE has collision enabled — verified in Task 003.)
3. **Do not shrink `InternalBufferCapacity` as a shortcut.** The 32 is sized to the
   per-tick hit cap so a pass never heap-allocates; shrinking it reintroduces the malloc
   removed in `collision-allocation-fix` (item 4).
4. **Reuse pooling is keyed per archetype.** Two archetypes ⇒ two dead-slot pools; impact
   and lingering slots are not interchangeable, so spawn routing must send each command
   to the matching pool.

## Tasks

| # | File | Scope | Depends on |
|---|---|---|---|
| 01 | [001-collision-handles-both-archetypes.md](001-collision-handles-both-archetypes.md) | `AoeCollisionSystem`: gate-strategy split (buffer vs `stackalloc`), drop buffer from required query | — |
| 02 | [002-two-archetypes-and-spawn-routing.md](002-two-archetypes-and-spawn-routing.md) | `AoeSpawnApplySystem`: two archetypes, keyed buckets, two dead-slot queries, reset routing | 01 |
| 03 | [003-sibling-systems-and-verification.md](003-sibling-systems-and-verification.md) | Verify lifetime/pulse-vfx systems, tests, footprint check | 01, 02 |

## Execution order

`01 → 02 → 03`. Task 01 first is **non-breaking**: until the impact archetype exists,
`WithNone<CombatLifetimeComponent>` matches nothing and `WithAll` matches every AOE, so
behavior is unchanged. Task 02 then introduces the impact archetype and routing, at which
point impact AOEs flow to the bufferless collision variant. Task 03 validates.

## Sequencing note (project-level)

Per the prior investigation, the dominant AOE spawn cost is `EntityCommandBuffer.Playback`
from cold-create running every frame; this footprint change only matters because
cold-create is hot. **Prefer fixing the reuse-pool mismatch first** — splitting archetypes
adds a second reuse pool and would otherwise complicate diagnosing that failure.

## Acceptance (whole plan)

- Impact (lifetime ≤ 0) AOE entities no longer carry `AoeContactGateElement`,
  `CombatLifetimeComponent`, or `AoePulseVfxComponent`; impact-archetype chunk capacity
  increases versus the old single archetype.
- Lingering repeat-hit gating unchanged; `OverlappingTargetRegisteredInMultipleCellsHitsOnce`
  and the lingering repeat-hit tests pass; all `AoeSimulationTests` pass.
- No per-tick `UnsafeUtility.Malloc` introduced by the impact de-dup (the `stackalloc`
  scratch is heap-free). This plan does not address the `NativeQueue` writer-block malloc
  — see [collision-writer-container-churn](../collision-writer-container-churn/index.md).
- Spawn reuse works for both pools; no impact AOE leaks (every impact AOE deactivates via
  the collision variant).

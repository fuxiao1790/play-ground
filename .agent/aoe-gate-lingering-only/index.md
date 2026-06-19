# Plan: Restrict AoeContactGateElement to Lingering AOEs

## Motivation

`AoeContactGateElement` is in the single AOE archetype built in
[AoeSpawnApplySystem.OnCreate](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L43),
so **every** AOE entity carries it. With
`[InternalBufferCapacity(CollisionConstants.MaxAoeTargetsPerTick)]` (= 32) added in
the `collision-allocation-fix` work, that is **256 bytes inlined per entity**
(32 × 8-byte `{int TargetId, float CooldownRemaining}`), whether or not the AOE
ever needs a cross-frame gate.

The retained, ticked contact gate is only meaningful for **lingering** AOEs —
the per-target repeat-hit cooldown in
[AoeContactGateSystem](../../Assets/Scripts/System/Aoe/AoeContactGateSystem.cs).
Pulse AOEs collide exactly once and deactivate
([AoeCollisionSystem.cs:260-263](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L260-L263));
visual-only AOEs never collide. For them the 256-byte buffer is dead weight that
is still allocated in-chunk, copied on every structural move (two
`AddSharedComponent` per cold-create), and shrinks chunk packing.

Goal: pulse / visual-only AOEs should not carry the across-frames gate buffer;
only lingering AOEs should.

## Why this is NOT a one-liner (constraints to honor)

1. **The collision job's signature requires the buffer.** `AoeCollisionJob` is an
   `IJobEntity` taking `DynamicBuffer<AoeContactGateElement> contactGates`
   ([AoeCollisionSystem.cs:163](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L163)).
   Removing the buffer from an archetype removes those entities from the query —
   they would stop colliding.

2. **Pulse AOEs use the gate *within* a single tick** for multi-cell de-dup
   ([AoeCollisionSystem.cs:203-239](../../Assets/Scripts/System/Aoe/AoeCollisionSystem.cs#L203-L239)),
   asserted by `OverlappingTargetRegisteredInMultipleCellsHitsOnce` in
   `AoeSimulationTests`. So a pulse AOE still needs a working de-dup mechanism for
   its pass; it just does not need the buffer **retained across frames**.

3. **Do not shrink `InternalBufferCapacity` as the "fix".** The 32 is sized to the
   per-tick hit cap precisely so a pulse hitting up to 32 targets never
   heap-allocates mid-tick. That is the malloc the `collision-allocation-fix` work
   removed; shrinking it reintroduces it. See
   [collision-allocation-fix/index.md](../collision-allocation-fix/index.md) item 4.

4. **Reuse pooling is keyed per archetype.** `AoeSpawnApplySystem` reuses disabled
   slots via a `WithDisabled<Active>` dead-slot query filtered by
   `(CombatRenderFaction, CombatRenderTypeId)`. A second archetype means a second
   dead-slot pool; pulse and lingering slots are not interchangeable, so the apply
   path must route each command to the right archetype/pool.

## Proposed approach (two archetypes)

Split AOEs into two archetypes at spawn time, decided by lifetime
(`cmd.Lifetime > 0f`, the same predicate already used to enable
`CombatLifetimeComponent` at
[AoeSpawnApplySystem.cs:249](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L249)):

- **Lingering archetype:** includes `AoeContactGateElement` (capacity 32), as today.
- **Pulse archetype:** omits `AoeContactGateElement`. Intra-tick de-dup for pulse
  is handled without a retained buffer (see open question Q1).

Touched systems:

- `AoeSpawnApplySystem` — two archetypes, two dead-slot queries, two reuse jobs;
  bucket key gains a pulse/lingering bit so commands route to the matching pool.
- `AoeCollisionSystem` — must process both archetypes. Either two job variants
  (one with the buffer, one without) or make the buffer optional and branch on
  presence.
- `AoeContactGateSystem` — already `WithAllRW<AoeContactGateElement>`, so it
  naturally only matches the lingering archetype; verify the query needs no change.

## Open questions (resolve before implementing)

- **Q1 — pulse intra-tick de-dup without a retained buffer.** Options: a
  `Temp`/stack-local dedup set inside `Execute` (re-creates a per-entity alloc —
  rejected by the malloc-fix goals), a small fixed inline scratch (e.g. stackalloc
  / fixed buffer capped at `MaxAoeTargetsPerTick`), or keep a buffer on pulse too
  but with a tiny inline capacity. Decide deliberately — this is the crux that
  determines whether the split is worth it.
- **Q2 — is the cross-frame gate the *only* per-entity difference?** If yes, two
  archetypes differing by one buffer is clean. Confirm no other lingering-only
  state should move with it.

## Relationship to the spawn-reuse investigation (do this FIRST)

This is a **cost-reduction / hygiene** change, not the fix for the 22–32 ms AOE
spawn cost. The dominant cost is `EntityCommandBuffer.Playback` because cold-create
runs every frame while reuse contributes ~0 (see profiling
`ProfilerCaptures/play-ground_2026-06-19_08-43-53.csv`). The 256-byte buffer only
matters because cold-create is hot; once reuse absorbs spawns (like the projectile
path, which shows zero playback), the per-entity weight stops mattering.

**Fix the reuse-pool mismatch before doing this split.** Splitting archetypes adds
a second reuse pool and would otherwise complicate diagnosing the reuse failure.

## Acceptance

- Pulse / visual-only AOE entities no longer carry `AoeContactGateElement`.
- Lingering AOE repeat-hit gating unchanged; all `AoeSimulationTests` pass,
  including `OverlappingTargetRegisteredInMultipleCellsHitsOnce` and the
  lingering repeat-hit tests.
- No new per-tick `UnsafeUtility.Malloc` in `AoeCollisionJob` (malloc-fix
  invariant preserved).
- Spawn reuse still works for both pools (cold-create not regressed).

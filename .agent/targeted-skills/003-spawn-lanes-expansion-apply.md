# 003 — Spawn lanes: expansion and apply

**Depends on:** 002. **Scope:** large. **Risk:** low — a direct mirror of an existing pair.

## Why

Gets targeted entities into the world through the canonical
event → expansion → command → apply path (C3). Mirrors
`ImpactAoeSpawnExpansionSystem` / `LingeringAoeSpawnExpansionSystem` and their apply systems
file-for-file; deviating from that shape is a review failure, not a style choice.

## New files

`Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`

- `TargetedExpansionCore.Expand(...)` — the shared body, mirroring `AoeExpansionCore.Expand`:
  1. Bail if `eventKind != expectedKind` or the template key misses.
  2. Stamp per-instance frame onto the template copy: faction, id, origin, **acquire anchor**,
     jitter seed, deterministic tick index.
  3. Loop `count = max(1, command.Count)`, emitting **one command per fork** (C3):
     - `TargetedId = TargetedIdFor(command, i)` — same hash shape `AoeIdFor` uses, so ids stay
       unique under deterministic tick expansion.
     - `InstanceIndex = i` — this is the whole fork-differentiation mechanism (requirements §3.6).
       **No positional scatter is applied.** There is no `ScatterRadius` field to read.
     - Seed `TargetedChainComponent`: `Origin` and `LinkSource`/`LinkTarget` all set to the event
       origin, `AcquireAnchor` from the event, `LastTargetKey = 0`, `LinkIndex = 0`,
       `LinkGateRemaining = 0` so link 0 fires on the first eligible update.
     - If `HasTimedSpawner != 0` and this is the interval variant, restamp
       `TimedSpawn.SourceId` and `TimedSpawn.JitterSeed` from the per-fork id, exactly as
       `AoeExpansionCore` does. The compiled template shares one seed across every cast, so
       per-instance restamping is what keeps each spawner's waves diverging.
     - Single-hit variant: clear `HasTimedSpawner`/`TimedSpawn`, mirroring impact AOE.
  4. Emit the spawn/arming VFX for the fork through `VfxEmit`, arming id when `ArmSeconds > 0`.
- `TargetedSpawnEventSingleton` and `LingeringTargetedSpawnEventSingleton` — queue + commands +
  `ProducerHandle` + `PendingHandle`, with the lane lifecycle comment (C6, C12).
- `TargetedSpawnExpansionSystem` and `LingeringTargetedSpawnExpansionSystem` — copy the AOE
  systems' `OnCreate` / `OnDestroy` / `OnUpdate` structure verbatim: create the queue and the scope
  query, drain queue + scope buffers into one `NativeArray`, read `TargetedSpawnTemplate`
  `[ReadOnly]`, schedule the expansion `IJob`, publish `PendingHandle`, dispose both containers in
  `OnDestroy` after completing both handles (C13).
- Ordering attributes mirror the AOE expansion systems: after `TimedSpawnSystem`, the collision
  systems, the new resolve systems, and `StatusProcessSystem`; before every apply system.

`Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`

- `TargetedSpawnApplySystem` and `LingeringTargetedSpawnApplySystem`, mirroring
  `ImpactAoeSpawnApplySystem` / `LingeringAoeSpawnApplySystem`.
- Two archetypes. Shared:
  `TargetedTag`, `TargetedIdentityComponent`, `TargetedChainComponent`, `TargetedResolveConfig`,
  `CombatHitPayload`, `AoeVfxIds`, `VfxTimingData`, `CombatLifetimeComponent`,
  `CombatRenderComponent`, `CombatRenderAuthoring`, `CombatRenderKindId`,
  `CombatKinematicsComponent`, `Active`, `ArmingTag`, `CombatArmingComponent`.
  Interval adds: `LingeringTargetedTag`, `TargetedTickGateComponent`, `TimedSpawnComponent`,
  `TimedSpawnStateComponent`.
- **Neither archetype carries `CombatCollisionComponent` or `CombatCollisionActiveTag`** — there is
  no hurtbox and no collision participation (requirements §6.2).
- Dead-slot queries: single-hit is `WithAll<TargetedTag>().WithDisabled<Active>()
  .WithNone<LingeringTargetedTag>()`; interval is the same with `.WithAll<LingeringTargetedTag>()`.
  This is exactly how the impact/lingering AOE pools are separated.
- Reuse before cold creation via `SpawnPoolTopUp.EnsureDisabledSlots` (C5).
- Increment `CombatStatsSingleton.EntitiesSpawned` by the applied count — the pool-cleanup
  calm-down gate derives despawns scene-wide from spawns, so an uncounted domain skews **every**
  pool's trimming, not just this one.

## Acceptance criteria

- EditMode: one `TargetedSpawnEvent` with a registered template and `Count = 1` produces exactly
  one entity carrying `TargetedTag` and not `LingeringTargetedTag`.
- EditMode: the lingering event produces an entity carrying both tags plus the tick gate.
- EditMode: `Count = 3` produces exactly three entities from one event, with distinct
  `TargetedId` values and `InstanceIndex` `0,1,2`, all at the **same** position — no scatter.
- EditMode: chain state on a fresh entity has `Origin == LinkSource == LinkTarget == event origin`,
  `AcquireAnchor == event anchor`, `LastTargetKey == 0`, `LinkIndex == 0`.
- EditMode: spawning, disabling, and respawning reuses the disabled slot rather than creating a new
  entity, per pool, and the two pools never borrow each other's slots.
- EditMode: an event whose `Kind` does not match the lane's expected kind produces no command.
- EditMode: a missing template key produces no command and does not throw.
- No `NativeArray`/`NativeList`/`NativeQueue` leaks — existing leak-detection test setup passes.

## Notes

Both expansion systems drain **both** the singleton queue and the scope buffers, because root casts
arrive via the scope buffer (through `ExternalSpawnGateSystem`) while internal producers enqueue
directly. Copying the AOE drain loop exactly is the safest route.

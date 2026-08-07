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
  2. Stamp the per-instance frame onto the template copy, mirroring `AoeExpansionCore.Stamp`:
     `Faction`, `TargetedId` (from `SourceId`), `Origin`, **`AcquireAnchor`**, `JitterSeed`,
     `DeterministicIdTickIndex`. The last two come from the event and are what step 3's id
     derivation reads back — the registered template leaves them default.
  3. Loop `count = max(1, command.Count)`, emitting **one command per fork** (C3):
     - `TargetedId = TargetedIdFor(command, i)` — copy `AoeIdFor`'s body exactly:
       `command.TargetedId + i` when `command.DeterministicIdTickIndex <= 0`, otherwise a hash of
       `TargetedId`, `JitterSeed`, `DeterministicIdTickIndex`, and `i`. This is why those two
       fields are on the command (task 002); without them, forks from a deterministically-expanded
       spawn collide on one id.
     - There is **no** `ScatterSeedFor` analogue. `AoeExpansionCore` needs one to place echoes in a
       random disk; targeted displaces nothing, so no RNG is constructed in this loop at all.
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
  4. Emit the fork's spawn or arming VFX through `VfxEmit.Enqueue`, mirroring
     `AoeExpansionCore`'s tail. The AOE version pulls its arguments from AOE-specific data, so
     each one needs an explicit targeted source:

     | `VfxEmit.Enqueue` argument | AOE source | Targeted source |
     |---|---|---|
     | `vfxId` | `AoeVfxIds.SpawnId` / `.ArmingId` | `TargetedVfxIds.SpawnId` / `.ArmingId`, arming when `ArmSeconds > 0` |
     | position | `AoeSpawnCommand.Position` (post-scatter) | the fork's origin — there is no scatter, so every fork emits at the same point |
     | `areaSize` | `AoeSpawnCommand.AreaSize` (gameplay area) | `TargetedVfxSizeComponent.EffectSize` — authored, visual-only; a chain has **no** gameplay area to derive a radius from |
     | `timing` | `AoeSpawnApplyUtility.VfxTimingFor` | `TargetedVfxUtility.TimingFor` (task 002) |

     Both queues (`PendingCircularSpawns`, `PendingTimedCircularSpawns`) are passed as parallel
     writers exactly as the AOE expansion job does; `VfxEmit` picks the queue from the id's
     decoded shape. A `0` id emits nothing, so an unauthored spawn or arming effect is free.
- `TargetedSpawnEventSingleton` and `LingeringTargetedSpawnEventSingleton` — queue + commands +
  `ProducerHandle` + `PendingHandle`, with the lane lifecycle comment (C6, C12).
- `TargetedSpawnExpansionSystem` and `LingeringTargetedSpawnExpansionSystem` — copy the AOE
  systems' `OnCreate` / `OnDestroy` / `OnUpdate` structure verbatim: create the queue and the scope
  query, drain queue + scope buffers into one `NativeArray`, read `TargetedSpawnTemplate`
  `[ReadOnly]`, schedule the expansion `IJob`, publish `PendingHandle`, dispose both containers in
  `OnDestroy` after completing both handles (C13).
- Ordering attributes mirror the AOE expansion systems **only for systems that already exist**:
  after `TimedSpawnSystem`, the projectile and AOE collision systems, and `StatusProcessSystem`;
  before every apply system.
- **Do not reference the resolve systems here.** They do not exist until task 004, and naming them
  in an `[UpdateAfter]` would not compile. The constraint is expressed from the other side: task
  004's resolve systems carry `[UpdateBefore(typeof(TargetedSpawnExpansionSystem))]` and
  `[UpdateBefore(typeof(LingeringTargetedSpawnExpansionSystem))]`, which orders the pair
  identically. No placeholder system types are created to satisfy an attribute.

`Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`

- `TargetedSpawnApplySystem` and `LingeringTargetedSpawnApplySystem`, mirroring
  `ImpactAoeSpawnApplySystem` / `LingeringAoeSpawnApplySystem`.
- Two archetypes. Shared:
  `TargetedTag`, `TargetedIdentityComponent`, `TargetedChainComponent`, `TargetedResolveConfig`,
  `CombatHitPayload`, `TargetedVfxIds`, `TargetedVfxSizeComponent`, `VfxTimingData`,
  `CombatLifetimeComponent`,
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
- EditMode: with `DeterministicIdTickIndex <= 0`, the three fork ids are `SourceId + 0,1,2`.
- EditMode: with `DeterministicIdTickIndex > 0`, the three fork ids are distinct and stable across
  runs for a fixed `(SourceId, JitterSeed, tickIndex)`, and two events differing only in tick index
  produce disjoint id sets — the collision case these fields exist to prevent.
- EditMode: chain state on a fresh entity has `Origin == LinkSource == LinkTarget == event origin`,
  `AcquireAnchor == event anchor`, `LastTargetKey == 0`, `LinkIndex == 0`.
- EditMode: spawning, disabling, and respawning reuses the disabled slot rather than creating a new
  entity, per pool, and the two pools never borrow each other's slots.
- EditMode: an event whose `Kind` does not match the lane's expected kind produces no command.
- EditMode: a missing template key produces no command and does not throw.
- EditMode: expansion with `ArmSeconds == 0` enqueues the `SpawnId` effect; with `ArmSeconds > 0`
  it enqueues `ArmingId` instead, both at `TargetedVfxSizeComponent.EffectSize` and at the fork's
  origin.
- EditMode: a `0` spawn id and a `0` arming id enqueue nothing and do not throw.
- EditMode: `Count = 3` enqueues three spawn effects at the same position — confirming no scatter.
- The project compiles after this task alone, with no resolve system present.
- No `NativeArray`/`NativeList`/`NativeQueue` leaks — existing leak-detection test setup passes.

## Notes

Both expansion systems drain **both** the singleton queue and the scope buffers, because root casts
arrive via the scope buffer (through `ExternalSpawnGateSystem`) while internal producers enqueue
directly. Copying the AOE drain loop exactly is the safest route.

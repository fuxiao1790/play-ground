# Internal Spawn Rework — Design Discussion

## Original Problem

ECS-originated follow-up spawns (impact AOEs, impact projectile bursts, AOE
projectile bursts) are deterministic consequences of ECS collision state, yet
they left simulation to become spawn requests again:

```
collision job → CombatPendingSpawn stream → CombatHitFlushJob (single IJob)
  → CombatSpawnElement buffer → CombatHitDispatchSystem (PresentationSystemGroup)
  → CombatSpawnRouter (managed) → root.Spawn() → ProjectileSpawnRequestElement /
  AoeSpawnRequestElement buffers → spawn systems (next frame)
```

Cost: a managed round-trip, a one-frame delay, and ECS depending on managed code
to spawn more ECS entities.

## What the First Rework Changed (implemented)

Moved internal follow-up spawns fully into ECS, same frame:

- `CombatSpawnConvertJob` (Burst `IJob`) reads the `CombatPendingSpawn` spawn
  stream and appends `ProjectileSpawnRequestElement` / `AoeSpawnRequestElement`
  to the destination scope buffers, using a `CombatSpawnRouting` component for
  source→destination routing and deterministic hash ids.
- Visuals (`VisualScale`/`VisualRotationDegrees`) now ride in the spawn
  snapshots (baked at authoring), not a managed render catalog — so the converter
  builds `CombatRenderComponent` without managed lookups.
- Deleted the managed route: `CombatSpawnRouter`, `CombatHitDispatchSystem` spawn
  replay, `CombatSpawnElement`, `HitSpawn` events, `CombatHitContext`/handlers.
  `CombatHitFlushJob` is now damage-only.
- Same-frame ordering: `ProjectileMultiExpandSystem`/`AoeSpawnSystem` run after
  both collision systems.

### What it did NOT change (the live critique)

The **threading model is identical**. The old `CombatHitFlushJob` was a single
`IJob` doing a scope-keyed scatter into buffers; `CombatSpawnConvertJob` is the
same single `IJob` doing the same scatter. Two of the four original findings are
still open:

- Lane ownership still collapses — collision writes parallel `NativeStream`
  lanes, then one serial job flattens them.
- Spawn systems still regroup on the main thread (`ProjectileSpawnBucket`
  dictionaries).

So the first rework was a **routing/ownership** win (no managed boundary, no
one-frame delay, Burst-built requests), not a **throughput** win.

## Why the Single-Threaded Collapse Exists

Both collision jobs query **globally** — all projectiles (or all AOEs) across all
factions in one parallel pass. One job mixes player-origin and mob-origin
entities, so every result record must carry a `Scope` entity to be routed back to
the correct per-scope buffer afterward. That routing — `Buffer[record.Scope].Add`
via `BufferLookup` random-access write — cannot run in parallel, so it is forced
into one serial `IJob`. **Scope-in-a-flat-record ⇒ serial scatter.** The `Scope`
field is the root cause.

Spawn data also currently leaves the `NativeStream` at the convert job (into a
`DynamicBuffer`), because that buffer is the shared ingestion point for all three
sources: `root.Spawn` (managed), `ProjectileChildSpawnSystem` (ECB), and the
convert job. Goal stated: keep collision-originated spawns in a `NativeStream`
from collision until the spawn system, never touching the buffer.

## Options Considered (to remove the scope-keyed scatter)

1. **ECB.ParallelWriter.AppendToBuffer from the collision job** (like
   `ProjectileChildSpawnSystem` already does). Parallel record, serial playback;
   drops the stream + serial convert. Scope becomes the append target, not a
   scatter key.
2. **Partition by scope** (`SetSharedComponentFilter` on `CombatRenderScope`):
   per-scope collision passes, output scope-local, no `Scope` field. Only ~4-way
   parallel.
3. **Result-on-entity**: damage aggregates onto target entities; spawns become
   spawn-request entities. Most scalable, most churn.

## Chosen Direction — World-as-Scope (faction per world)

Move mob→player projectiles/AOEs into a **separate ECS world** from player→mob.
This dissolves the problem instead of working around it.

- **2 worlds**: player-origin, mob-origin. Each world is a single faction
  direction, so a collision job in a world never mixes factions.
- **1 unified scope singleton per world.** Projectile and AOE do not need
  separate scopes — they share one scope holding the **single** target list
  (`CombatTargetElement`, intended identical for projectile and AOE — already
  wired: `GameRoot.BindCombatScopes` binds proj+aoe roots to the same target-set
  key), the shared `CombatDamageElement`, and the
  `ProjectileSpawnRequestElement` / `AoeSpawnRequestElement` /
  `VfxSpawnRequestElement` buffers.
- Collision reads the target list via `GetSingleton`; writes damage/spawns to the
  singleton (or straight to streams). Destination domain is chosen by payload
  type (`ImpactAoe` → AOE; `ImpactProjectile`/burst → projectile), not a per-record
  entity.

### What this removes

- The `Scope` field on records and on `ProjectileIdentityComponent` (vestigial).
- The `ProjectileScope` vs `AoeScope` distinction (one scope per world).
- `CombatSpawnRouting` (routing is "this world's singletons").
- The scope-keyed serial scatter — collision→spawn can flow as direct
  `NativeStream`s consumed by the spawn systems (the stated goal falls out, since
  there is nothing to demux).
- Duplicate target buffers (one shared list instead of per-domain copies).

### What it costs (the real work — ownership/setup, not the hot path)

1. **Scope ownership moves from per-root to per-world.** Today each
   `ProjectileRoot`/`AoeRoot` creates its own scope in `BindWorld`. New: one
   owner per world (natural fit: `CombatRuntimeRoot`) creates the unified scope;
   proj/aoe roots attach to the world and register types/targets into it.
2. **`CombatEcsWorld` becomes multi-world**, keyed by faction; roots acquire the
   right world. Each world added to the player loop, systems instantiated per
   world.
3. **Drop `*.Scope` reads** in collision/tracking — they fetch the target buffer;
   move to the singleton. Wide but mechanical.
4. **Unified target sync + damage replay per world** instead of per root.
5. **Test rework** — sim tests build scopes by hand
   (`CreateEntity(typeof(ProjectileScope))`, per-scope `AddBuffer`); those change.

This steady-state is clearly better and removes the disliked `Scope` routing, but
it is a structural refactor of scope/world ownership plus test churn — larger than
everything done so far combined.

### Open invariants to confirm before planning

1. **Projectile and AOE always target the same set within a faction.** If a future
   AOE must hit a different layer, the unified target list breaks and they'd need
   separating again. Treat as a hard invariant?
2. **Actors that must be targets in both worlds** (e.g. a neutral destructible hit
   by both factions) must register into both worlds. Acceptable?
3. **World count**: exactly two faction worlds, or a general world-per-faction-key
   registry? World-per-faction stops scaling at many factions (fixed per-world
   overhead); in-world partitioning would be the fallback then.

## Status

First rework (ECS-owned internal spawns, managed route deleted) is implemented but
**not yet compiled/tested** — the Unity editor held the project lock. The
world-as-scope direction above is the agreed next step, pending the three open
invariants and a written plan.

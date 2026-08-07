# Targeted Chain System

Targeted chains are high-count ECS combat entities. Use
[index.md](./index.md) for the short map. This page mirrors the AOE system doc.

## Summary

A targeted entity walks target proxies. It has no gameplay collider and does
not use Physics2D. There is exactly **one** targeted variant: a chain lives for
its walk and no longer. It expires the instant it uses its last chain, or the
instant a link finds nothing to jump to.

Repeating chains are composed, not authored: hang a
`TargetedIntervalSpawnTrigger` off a projectile or lingering AOE and the source
spawns a fresh chain per energy tick, the same way every other repeating child
works.

Main files:

- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`: the event and the
  command shape.
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`: the event
  lane and echo fan-out.
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`: the pool and
  materialization.
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`: the spatial-hash
  walk, rank selection, falloff, caps, VFX emission, and expiry.

## Authored Shape

Four numbers describe a chain:

| Field | Meaning |
|---|---|
| `echoCount` | How many separate chains one cast creates. |
| `chainCount` | How many links **one** chain walks. |
| `chainDistance` | How far each hop reaches — link 0 included. |
| `chainDelay` | Seconds between links. `0` resolves the whole walk in one update. |

Plus `damage`, `manaCost`, `directDamageEnabled`, `chainDamageFalloff`,
`armSeconds`, and `prefab`.

There is **no authored lifetime and no tick interval.** Those two used to sit
alongside `chainDelay` and had to be reconciled against it by hand, which was
the main source of silent no-op skills. `RuntimeTargetedDefinition.LifetimeFor`
derives a fail-safe `CombatLifetimeComponent` from `chainCount * chainDelay`
plus a margin; it exists only so a stalled instance cannot leak a pooled slot,
and in normal play the resolve always expires the instance first.

## Archetypes And Pool

Every chain has `TargetedTag`, identity, walk state, resolve config, hit
payload, render mirror, VFX ids/sizes, `Active`, arming, and lifetime
components. One archetype, one disabled-slot pool. Each apply pass first tops up
the archetype, then reuses disabled `Active` slots in deterministic chunk order.
Hot despawn only disables `Active`; cleanup trims bounded excess slots later.

## Spawn And Resolve

`TargetedSpawnEvent` carries template key, origin, acquire anchor, faction,
source id, and deterministic frame. Expansion stamps the instance frame and
emits one `TargetedSpawnCommand` per echo.

The resolve system runs after target spatial-hash build and arming, before hit
finalize and spawn expansion. It searches hostile target proxies only:

1. Link 0 searches from `AcquireAnchor`; later links search from the last
   target. Both use `ChainDistance` — one authored reach, no second radius.
2. Fork `i` of an echo opens on the `i`-th nearest eligible target so multiple
   chains from one cast do not walk the same path.
3. Only the immediately prior target is excluded; older targets may be
   revisited, so two enemies and `chainCount = 6` gives A→B→A→B→A→B.
4. Each accepted link emits `CombatHitEvent` with `DamageScale` equal to
   `chainDamageFalloff^linkIndex`, a hit VFX request, and a line segment.

`chainDelay` gates links. The resolver consumes overdue gates in one update, so
long frames catch up without inventing extra frames. When `LinkIndex` reaches
`ChainCount`, or a link finds no target, the resolve disables `Active` and emits
the expire VFX — the walk is the lifetime.

## Render Mirror And VFX

Chains carry shared render components so the common batched renderer can draw a
debug sprite when authored. `CombatKinematicsComponent` is only a mirror of the
last resolved link, not authoritative walk state. Link VFX uses `LineSegment`
requests with start position, end position, and authored link width; targeted
chains have no gameplay area from which width or effect size can be derived.

## Caps

Authoring clamps `chainCount` to 1..32. Resolve defensively clamps to the same
`MaxChainCount = 32` limit. Timed-child sources emit at most 256 children per
update. Pool cleanup is bounded and runs only after spawn apply with frame
headroom.

## Timing Note

Targeted resolve emits raw hits before `CombatApplyFinalizeSingleSystem`.
Health is therefore stale within a multi-link walk by design: chains may
overkill targets that already have enough queued damage this frame.

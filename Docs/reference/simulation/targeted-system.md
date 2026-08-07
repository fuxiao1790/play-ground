# Targeted Chain System

Targeted chains are high-count ECS combat entities. Use
[index.md](./index.md) for the short map. This page mirrors the AOE system doc.

## Summary

A targeted entity walks target proxies. It has no gameplay collider and does
not use Physics2D. A single-hit chain resolves once then returns to its pool.
A lingering targeted chain restarts a walk every tick until its lifetime ends.

Main files:

- `Assets/Scripts/System/Targeted/TargetedSpawnPipeline.cs`: the two events,
  one command shape, and lifetime variant helper.
- `Assets/Scripts/System/Targeted/TargetedSpawnExpansionSystem.cs`: targeted
  and lingering-targeted event lanes and expansion.
- `Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs`: two pools and
  materialization.
- `Assets/Scripts/System/Targeted/TargetedResolveSystem.cs`: single-hit and
  interval resolve systems.
- `Assets/Scripts/System/Targeted/TargetedResolveCore.cs`: spatial-hash walk,
  rank selection, falloff, caps, and VFX emission.

## Archetypes And Pools

Every chain has `TargetedTag`, identity, walk state, resolve config, hit payload,
render mirror, VFX ids/sizes, `Active`, arming, and lifetime components.
Lingering chains also have `LingeringTargetedTag` and `TargetedTickGateComponent`.
The tag absence is the single-hit discriminator.

`TargetedSpawnApplySystem` and `LingeringTargetedSpawnApplySystem` have separate
disabled-slot pools. Each apply pass first tops up the needed archetype, then
reuses disabled `Active` slots in deterministic chunk order. Hot despawn only
disables `Active`; cleanup trims bounded excess slots later.

## Spawn And Resolve

`TargetedSpawnEvent` and `LingeringTargetedSpawnEvent` carry template key,
origin, acquire anchor, faction, source id, and deterministic frame. Expansion
stamps the instance frame and emits one `TargetedSpawnCommand` per chain count.
`lifetimeSeconds > 0` selects the lingering event lane.

The resolve system runs after target spatial-hash build and arming, before hit
finalize and spawn expansion. It searches hostile target proxies only:

1. First link searches from `AcquireAnchor` within `AcquireRadius`.
2. Later links search from the last target within `ChainRadius`.
3. Only the immediately prior target is excluded; a restart may use it if no
   alternative exists, and older targets may be revisited.
4. Each accepted link emits `CombatHitEvent` with `DamageScale` equal to
   `chainDamageFalloff^linkIndex`, a hit VFX request, and a line segment.

`chainDelaySeconds` gates links. The resolver consumes overdue gates in one
update, so long frames catch up without inventing extra frames. A single-hit
chain dies after the walk terminates. A lingering chain resets walk state when
its tick gate expires, and lifetime can end it mid-walk.

## Render Mirror And VFX

Chains carry shared render components so the common batched renderer can draw a
debug sprite when authored. `CombatKinematicsComponent` is only a mirror of the
last resolved link, not authoritative walk state. Link VFX uses `LineSegment`
requests with start position, end position, and authored link width; targeted
chains have no gameplay area from which width or effect size can be derived.

## Caps

Authoring clamps `maxTargets` to 1..32. Resolve defensively clamps to the same
`MaxChainTargets = 32` limit. Timed-child sources emit at most 256 children per
update. Pool cleanup is bounded and runs only after spawn apply with frame
headroom.

## Timing Note

Targeted resolve emits raw hits before `CombatApplyFinalizeSingleSystem`.
Health is therefore stale within a multi-link walk by design: chains may
overkill targets that already have enough queued damage this frame.

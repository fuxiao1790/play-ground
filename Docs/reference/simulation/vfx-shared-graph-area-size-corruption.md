# Shared VFX Graph Area-Size Corruption

This document records a confirmed, recurring VFX Graph failure mode: multiple
skill sets use the same `VisualEffectAsset` but emit different `AreaSize`
values, and alive particles from one skill set render with another skill set's
area size.

This is not a particle-lifetime or animation-reset problem. Particle lifetime
only determines how long an affected particle remains available to be
corrupted. The defect is cross-skill area-size aliasing through one shared graph
instance and its reused `AreaSizes` upload buffer.

## Non-Negotiable Area-Size Rule

`AreaSizes` contains values for the current dispatch batch. It is not stable
area-size storage for particles that are already alive.

```text
request buffers -> Initialize Particles -> persistent particle attributes
persistent particle attributes -> Update/Output
```

Each particle must copy its `AreaSizes[spawnIndex]` value into its own particle
attribute during `Initialize Particles`. `Update Particle` and `Output Particle`
must calculate size from that stored attribute and must not sample `AreaSizes`.

## Why Different Skill Sets Interfere

One `VisualEffectAsset` has one runtime `VisualEffect` instance. Every skill
set, AOE type, and event slot registered with that asset sends requests to that
same instance.

For each graph dispatch, presentation:

1. Buckets the current frame's requests by `VfxId`.
2. Uploads the bucket into the graph's reusable graphics buffers.
3. Sets `SpawnCount` to the current bucket length.
4. Sends `OnSpawn` once.

The next non-empty dispatch for that graph overwrites the same `AreaSizes`
elements and changes `SpawnCount`. Buffer growth changes capacity only; it does
not assign permanent area-size storage to each particle or skill set.

`spawnIndex` identifies an entry in the batch currently being initialized. It
is not a stable VFX instance id and does not identify the same request in a
later upload.

## Exact Failure Mechanism

Assume particle `P` was born in batch `A` with `spawnIndex = i` and area size
`A.AreaSizes[i]`.

The broken graph keeps this expression connected to Update or Output:

```text
AreaSizes[clamp(spawnIndex, 0, SpawnCount - 1)]
```

Later, batch `B` overwrites `AreaSizes` and `SpawnCount`. Particle `P` is still
alive, but it now evaluates:

```text
B.AreaSizes[clamp(i, 0, B.SpawnCount - 1)]
```

That value belongs to an unrelated request. If `i` is outside the new batch,
clamping makes many old particles read the final entry in batch `B`. Clamping
prevents an invalid index; it does not restore the birth-batch association.

This incident specifically concerns `AreaSizes`: alive particles change scale
for one or more frames when another skill set's differently sized requests
occupy the reused indices. Other request buffers follow the same transient
transport rule, but they are not the confirmed issue described here.

## Confirmed Incident

The original reproducible case used one Vortex graph shared by two Vortex skill
sets with different final AOE sizes.

- `BenchmarkAllAoeLoadout` appeared correct because its Vortex traffic used one
  size.
- `BenchmarkHybridLoadout` contained two skill sets using the same Vortex asset
  with different sizes.
- An already alive Vortex shrank for one frame and returned to its prior size on
  the next frame.
- Reordering skills changed how often the artifact appeared because it changed
  which size occupied a reused buffer index on a given dispatch.
- Using two skill sets with different sizes reproduced the artifact reliably.
- Using the same graph under higher load with one size did not reproduce it.

The one-frame shrink is the key signature. Particle age, lifetime, and animation
state were not reset. The particle briefly rendered with another skill set's
`AreaSize`, then read a different value after the next upload.

## Misleading Tests And False Leads

These changes do not address this failure:

- Changing the texture. The bad value is per-particle payload data.
- Changing particle lifetime. It changes the observation window, not which
  skill set's area size an alive particle reads.
- Removing pulse VFX. Any later spawn batch for the same graph can overwrite
  the buffers.
- Raising or removing a request cap. Rejected requests cannot mutate existing
  particles, and accepted later batches still reuse the buffers.
- Comparing total AOE or particle load. Mixed payload values matter; a much
  larger same-value workload can hide the bug.
- Giving trigger types separate VFX identities. Trigger is not graph identity,
  and separate instances only mask broken area-size isolation.
- Duplicating the graph asset. Separate buffers hide cross-stream corruption but
  preserve the broken authoring pattern.

A same-value test is especially weak. Existing particles may read the wrong
batch every frame without a visible difference when every entry has the same
value.

## Correct Graph Pattern

For a Basic graph:

```text
Initialize Particles
  index = clamp(spawnIndex, 0, SpawnCount - 1)
  position = Positions[index]
  base size = AreaSizes[index]

Update Particle
  use age, lifetime, random values, and stored particle attributes only

Output Particle
  rendered size = stored base size * size-over-life curve
```

Basic and Timed graphs use the same area-size rule. Timed shape selection does
not isolate two skill sets that share one graph asset.

When moving size sampling from Output to Initialize, preserve composition:

- Initialize sets the persistent base size from `AreaSizes[index]`.
- Output multiplies the stored base size by the size-over-life curve.
- An Output curve configured as `Set` can overwrite the stored area scale and
  produce a different visual even though cross-skill corruption is fixed.

## Required Regression Test

Every graph that consumes per-request buffers must be tested with old particles
alive while later batches carry different values.

Minimum area-size test:

1. Use one graph asset and one runtime graph instance.
2. Spawn a long-lived particle with area size `A`.
3. Before it dies, dispatch another particle with area size `B`, where `A != B`.
4. Alternate `A` and `B` across frames and vary batch counts.
5. Verify the first particle keeps its original base size and continues only
   its authored size-over-life animation.

Request order should also be changed because parallel producers and
counting-sort buckets do not provide persistent per-particle ordering.

## Review Checklist

Before accepting a VFX graph or VFX dispatch refactor:

- Trace the `Sample Buffer` node connected to `AreaSizes`.
- Confirm its sampled value is consumed only by Initialize.
- Confirm Update and Output calculate size from a stored particle attribute,
  not directly from `AreaSizes`.
- Confirm `SpawnCount` only drives the current `OnSpawn` burst and bounds
  Initialize samples, and confirm `spawnIndex` is not used after Initialize.
- Test one graph shared by multiple skill sets with different area sizes.
- Test different batch counts and request ordering while old particles remain
  alive.
- Do not use high same-value load as proof of correctness.

## Validation Gap

Runtime graph validation currently checks exposed property names and types. It
cannot prove where `AreaSizes` is sampled inside VFX Graph. A graph can pass
registration while still allowing skill sets to corrupt each other's size.

Until an editor validator can inspect graph context dependencies, mixed-value
visual regression testing and manual `Sample Buffer` connection review are
required.

## Current Code Anchors

- [`VfxDataShapes.cs`](../../../Assets/Scripts/System/Vfx/VfxDataShapes.cs)
  defines Basic and Timed request payloads and exposed buffer names.
- [`CombatAoeVfxDispatchSystem.cs`](../../../Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs)
  builds the current-frame graph buckets.
- [`CombatAoeVfxDispatcher.cs`](../../../Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs)
  overwrites reusable buffers and sends `OnSpawn`.
- [`CombatVfxRoot.cs`](../../../Assets/Scripts/System/Vfx/CombatVfxRoot.cs)
  owns one runtime instance and resource set per registered graph asset.

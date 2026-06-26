# 002 — Refactor `*SpawnEvent` to a thin registry link

## Goal

Refactor the existing `ProjectileSpawnEvent` / `AoeSpawnEvent` into the thin runtime
spawn event — a registry link plus the per-instance frame, no spawn data — and route
it through the existing queues / scope submission buffer. No new event type is added;
the event keeps its name and its role as the queue payload, just slimmed.

## Why refactor instead of adding a type

Today `*SpawnEvent` is overloaded: it is both the queue payload and the registry
template. The unified model needs the queue payload thin (a link) while the template
stays fat, so the two diverge. Rather than add a parallel `SpawnInvocation` and leave
a fat `*SpawnEvent` behind, refactor `*SpawnEvent` to the thin shape (its true role)
and move the fat data to the registry template (see 001).

This also removes a redundant copy. If the registry template stayed event-shaped
while apply consumes a command, expansion would remap event→command field-by-field
per shot — two fat layouts of the same spawn plus a conversion. Storing the template
in **command shape** (see 001) makes expansion a copy + stamp + explode with no remap.

## Thin event shape

```
ProjectileSpawnEvent / AoeSpawnEvent (thin):
  IntervalChildKind Kind          // projectile / aoe (enables one unified queue)
  Hash128           TemplateKey
  float2            Position
  float2            AimDirection
  CombatFaction     Faction
  int               SourceId
  uint              JitterSeed
  int               DeterministicIdTickIndex
  int               ContactGateSeedTargetId
```

## Changes

- Strip the fat fields (count/spread/speed/lifetime/radius/shape/hit payload/
  tracking/render/timed/follow-up snapshots) off `ProjectileSpawnEvent` /
  `AoeSpawnEvent`; those now live on the command-shaped template in 001.
- Producers enqueue the thin event; the scope `DynamicBuffer<*SpawnEvent>` submission
  buffer carries the thin event for managed root casts.
- Decide per open-question #2 whether the two domain events collapse into one
  thin event carrying `Kind` (recommended) or stay separate.

## Acceptance criteria

- `*SpawnEvent` carries only the link + instance frame; no volley, hit payload, or
  follow-up data.
- `sizeof` is small (well under a cache line target).
- Nothing on the runtime spawn path holds two fat representations of one spawn.

## Dependencies

001 (the command-shaped template the thin event references).

## Scope

Medium. Slims an existing type; field removals ripple to producers (wired in 003/005).

## Changes

- New blittable struct `SpawnInvocation`:
  - `IntervalChildKind Kind` (projectile / aoe)
  - `Hash128 TemplateKey`
  - `float2 Position`
  - `float2 AimDirection`
  - `CombatFaction Faction`
  - `int SourceId`
  - `uint JitterSeed`
  - `int DeterministicIdTickIndex`
  - `int ContactGateSeedTargetId`
- Producers enqueue `SpawnInvocation` instead of `ProjectileSpawnEvent` /
  `AoeSpawnEvent`. Decide per open-question #2 whether this is one unified queue
  or two; default to one invocation type with a single submission path.
- The scope `DynamicBuffer<AoeSpawnEvent>` managed-submission buffer becomes a
  `DynamicBuffer<SpawnInvocation>` (or equivalent), since managed root casts now
  submit invocations.

## Acceptance criteria

- `SpawnInvocation` is unmanaged / Burst-compatible.
- Nothing on the runtime spawn path carries volley params, hit payloads, or
  follow-up data — only the link + instance frame.
- `sizeof(SpawnInvocation)` is small (well under a cache line target).

## Dependencies

001 (registry holds the template the invocation references).

## Scope

Medium. New struct + queue/buffer type changes ripple to producers (wired in 003/005).

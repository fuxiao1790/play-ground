# 002 — Slim `SpawnInvocation` event

## Goal

Introduce the slim runtime spawn event that carries only a registry link plus the
per-instance frame, and route it through the existing queues / scope submission
buffer.

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

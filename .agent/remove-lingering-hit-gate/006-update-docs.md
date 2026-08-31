---
name: update-docs
description: Remove stale lingering-AOE collision-cadence documentation while preserving projectile repeat-hit cooldown documentation.
---

# 006 - Update Docs

## Depends On

Tasks 001-004 must land first so documentation describes implemented behavior.

## Changes

### [Docs/reference/simulation/aoe-system.md](../../Docs/reference/simulation/aoe-system.md)

1. Update summary/main-file descriptions around lines 34 and 57 so
   `tickIntervalSeconds` means pulse-VFX timing, not collision countdown.
2. Update the `AoeSpawnCommand` description around line 126: replace stale
   `repeat cooldown` wording with the VFX interval's actual purpose.
3. Remove `AoeHitGateComponent` from the required-component list around line
   181.
4. Replace lines 225-229 with the new contract: active lingering AOEs call
   `RunCollision` every simulation tick, without per-entity interval gating.
   `TickIntervalSeconds` controls only pulse-VFX cadence through
   `AoePulseVfxComponent` and `VfxTimingData`; it does not affect collision or
   damage frequency.

### Other stale AOE cadence descriptions

Update these AOE-specific descriptions:

- [Docs/flows/runtime-frame.md:19](../../Docs/flows/runtime-frame.md#L19) — separate pulse-VFX interval timing from every-tick lingering collision.
- [Docs/reference/simulation/spawn-template-registry.md:395-396](../../Docs/reference/simulation/spawn-template-registry.md#L395-L396) — remove the statement that lingering AOEs tick from collision interval state.
- [Docs/reference/architecture/architecture.md:132-133](../../Docs/reference/architecture/architecture.md#L132-L133) — remove tick-interval countdown from `LingeringAoeCollisionSystem` responsibilities.
- [Docs/testing.md:54-55](../../Docs/testing.md#L54-L55) — describe lingering every-tick and re-entry behavior without implying collision cooldown.
- [Docs/performance.md:90](../../Docs/performance.md#L90) — clarify that lingering overlap fields collide every simulation tick.
- [Docs/reference/game-logic/skill-system.md:336-339](../../Docs/reference/game-logic/skill-system.md#L336-L339) — identify `tickIntervalSeconds` as pulse-VFX timing rather than damage cadence.

Do not change projectile repeat-hit cooldown documentation. In particular,
`ProjectileHitComponent.RepeatHitCooldownSeconds` in
`Docs/reference/simulation/spawn-template-registry.md` remains valid.

## Acceptance Criteria

- No `AoeHitGateComponent` mention remains under `Docs/`.
- No AOE documentation describes `RepeatHitCooldownSeconds`, collision
  tick-interval countdown, or interval-gated lingering hits.
- Projectile `RepeatHitCooldownSeconds` documentation remains unchanged.
- AOE documentation describes lingering collision every simulation tick while
  active, without per-entity throttling.
- AOE documentation identifies `TickIntervalSeconds` as VFX-only timing.

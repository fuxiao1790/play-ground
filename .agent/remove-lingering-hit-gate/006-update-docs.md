---
name: update-docs
description: Update Docs/reference/simulation/aoe-system.md to remove AoeHitGateComponent and describe every-tick lingering collision.
---

# 006 - Update Docs

## Depends On

All of [001](001-remove-gate-from-lingering-collision.md)-[004](004-rename-tick-interval-field.md)
should be implemented first so the doc describes the landed behavior, not a
plan.

## Changes

### [Docs/reference/simulation/aoe-system.md](../../Docs/reference/simulation/aoe-system.md)

1. [Line 181](../../Docs/reference/simulation/aoe-system.md#L181) — remove
   `AoeHitGateComponent` from the required-component list.
2. [Lines 226-229](../../Docs/reference/simulation/aoe-system.md#L226-L229)
   — currently:
   > `AoeHitGateComponent.Remaining` prevents another collision pass until
   > the AOE tick interval expires.
   >
   > There is no global AOE tick. Repeat timing belongs to each lingering AOE.

   Replace with a description matching the new behavior: lingering AOEs run
   `RunCollision` every simulation tick they're active (no per-entity
   throttle), and `TickIntervalSeconds` (renamed per task 004) now controls
   only the pulse-VFX flash cadence via `AoePulseVfxComponent`/`VfxTimingData`
   — it has no effect on collision/damage frequency.

## Acceptance Criteria

- No remaining mention of `AoeHitGateComponent` or `RepeatHitCooldownSeconds`
  anywhere under `Docs/`.
- The doc's description of lingering AOE collision cadence matches the
  implemented behavior exactly (every tick, no gate).
- The doc clarifies `TickIntervalSeconds`'s narrowed, VFX-only scope so a
  future reader doesn't reintroduce a collision-cadence assumption from the
  field's name/history.

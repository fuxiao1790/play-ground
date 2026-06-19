# 003 — Sibling systems + verification

**Depends on:** 001, 002. **Scope:** verification (+ minor cleanups). **Complexity:** low.

## Sibling systems — confirm they fall out correctly

- **`AoeContactGateSystem`** — already `WithAllRW<AoeContactGateElement>`; with the buffer
  only on the lingering archetype it matches lingering exclusively. Confirm no query change.
- **`CombatLifetimeSystem.AoeLifetimeJob`** ([:98](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L98))
  — `[WithAll(AoeTag, Active, CombatLifetimeComponent)]`, enabled-filtered. Impact AOEs lack
  the component → excluded (as they were when it was disabled). Confirm no change.
- **`AoePulseVfxSystem.AoePulseVfxJob`** ([:43-58](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs#L43))
  — reads `ref AoePulseVfxComponent` and `EnabledRefRO<CombatLifetimeComponent>`; both absent
  on impact → impact auto-excluded. The `!lifetimeEnabled.ValueRO` guard is now always false
  on lingering; optionally drop it (keep the `pulseVfx.Interval <= 0f` guard). Optional cleanup.

## Verify constraint 2 (no impact leak)

- Confirm every impact (lifetime ≤ 0) AOE produced has collision enabled
  (`NeedsCollision(cmd)` true), so the impact collision variant deactivates it. A degenerate
  impact AOE with no damage/projectile/lifetime/collision would never deactivate — assert this
  config does not exist (authoring/spawn data), or add a guard.

## Tests

- `OverlappingTargetRegisteredInMultipleCellsHitsOnce` (`AoeSimulationTests`) — now exercises
  the **impact** `stackalloc` de-dup path; must still hit once.
- Lingering repeat-hit / cooldown tests — unchanged behavior on the lingering archetype.
- A reuse test per pool: spawn impact then lingering of the same `(faction, typeId)` and
  confirm slots are not cross-claimed and both reuse on subsequent spawns.
- Full `AoeSimulationTests` + projectile collision suites green.

## Footprint check (the actual goal)

- Assert the impact archetype's chunk capacity is higher than the old single archetype's
  (buffer + two components removed). Inspect via `EntityManager.GetChunk(...).Capacity` /
  archetype `ChunkCapacity` in an edit-mode test or a one-off debug log, and record the
  before/after in the PR.

## Acceptance

- All sibling systems confirmed correct with no behavior change (cleanups optional).
- All listed tests pass.
- Impact archetype chunk capacity increase documented; no impact-AOE leak.

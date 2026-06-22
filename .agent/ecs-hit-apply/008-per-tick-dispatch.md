# 008 — Per-tick aggregated dispatch; GameObject mirrors HP

**Change:** adapt · **Depends:** 007 · **Scope:** medium

## Goal

Replace the per-hit managed replay with one aggregated dispatch per target per
tick. This is the change that removes `CombatApplyBridge.HitReplay`'s O(hits)
cost.

## Changes

1. **`ICombatTarget.ReceiveCombatTick`** — new combined per-tick entry
   ([ICombatTarget.cs](../../Assets/Scripts/System/Common/ICombatTarget.cs)):
   ```csharp
   void ReceiveCombatTick(in CombatTickResult result,
                          IReadOnlyList<StatusStackSnapshot> stacks);
   ```
   Retire `ReceiveCombat`/`ReceiveHits`/per-hit `ReceiveHit` from the hot path
   (keep `ReceiveHit` only if other callers still need it; the bridge no longer
   calls it).

2. **`CombatApplyBridge.ReplayCombat`** ([CombatApplyBridge.cs:462-506](../../Assets/Scripts/System/Common/CombatApplyBridge.cs#L462))
   — iterate `CombatTickResult[]` (one per target), resolve `TargetCompanion`,
   build the status list from the snapshot slice, call `ReceiveCombatTick` once.
   No per-hit `CombatHitData` list. Keep the `IsTargetUsable`/resolve guards.

3. **`MobRoot.ReceiveCombatTick`** ([MobRoot.cs:275-309](../../Assets/Scripts/Mob/MobRoot.cs#L275))
   — set `CurrentHealth = result.Health` (mirror ECS), update blackboard; if
   `result.Health <= 0` → `Died`/`SoftDie` (GameObject decides death). Drive
   feedback from the aggregate: `RequestHurt` once if `DamageTaken > 0`, push a
   `Damaged`/`CritDamaged` event using `HitCount`/`CritCount` (one event with
   counts, or N if `MobStateDriver` is count-sensitive — pick and document).
   Apply the status list as today.

4. **`PlayerRoot.ReceiveCombatTick`** ([PlayerRoot.cs:210-229](../../Assets/Scripts/Player/PlayerRoot.cs#L210))
   — mirror `result.Health` into `PlayerHealth`; on `<= 0` run the existing death
   path (`QueueCombatTargetProxyDelete`). `PlayerHealth.TakeDamage` stops mutating
   HP (mirror-set only).

5. **`MobRoot.TakeDamage`** becomes mirror-only (no independent subtraction); the
   `StatusEffects.Initialize(d => TakeDamage(d), …)` wire is left as a stub since
   no DoT system is active. If DoT is built later it must enqueue a
   `CombatHitEvent` instead (documented in index).

## Acceptance criteria

- **Push happens in the dedicated sync system only** — `CombatApplyBridge`
  (presentation), separate from the sim-side `CombatApplyFinalizeSystem`. The sim
  system never calls into managed targets.
- **HP and status both cross through that one system**, in a single
  `ReceiveCombatTick(result, stacks)` per target.
- `CombatApplyBridge.HitReplay` cost scales with **target count**, not hit count.
- GameObject HP exactly mirrors ECS `TargetHealth.Current` each tick; death is
  decided by the GameObject from the pushed value.
- Hurt/crit feedback still fires (driven by `DamageTaken`/`HitCount`/`CritCount`).

## Notes / risks

- One `ReceiveCombatTick` call per target replaces up to thousands of
  `ReceiveHit` calls — this is the perf payoff.
- Decide the `MobStateDriver` feedback granularity (single aggregated event vs N
  events). Default: single event carrying `HitCount`/`CritCount`; revisit if a
  trigger needs per-hit cadence.
- Mob/player death timing shifts to "GameObject reacts to pushed HP<=0" — a frame
  later than the ECS subtraction at most; acceptable.

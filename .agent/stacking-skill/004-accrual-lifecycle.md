# 004 — StackAccrualSystem owns the full lifecycle

## Change kind: adapt

## Structural role
`StackAccrualSystem` becomes the single owner of the target stack buffer's lifecycle:
expire, accumulate, detonate, clear. No forwarding (there is no chain).

## Behavior (per OnUpdate)
1. **Tick + fizzle.** Decrement `LifetimeRemaining` on every `TargetStackEntry` by delta;
   remove entries that reach 0 while `Count < Threshold` (discard, no detonation).
2. **Apply.** Drain `StackApplyEvent`s grouped by `(TargetProxy, DebuffKey)`; for each,
   find/create the entry, add 1 to `Count`, add the per-stack contribution to the summed
   fields, store the `DetonationSnapshot` (first write), and **refresh** `LifetimeRemaining`
   to the event's `Lifetime`.
3. **Detonate.** When `Count >= Threshold`, hand the stored `DetonationSnapshot` + summed
   contributions to a `BuildDetonationSpawn(snapshot, summed, position)` helper that
   **dispatches on `snapshot.Kind`** and enqueues the matching spawn event; then remove the
   entry. The accrual itself stays detonation-kind agnostic. Implement the **AOE** case now;
   the projectile case is the single localized extension point (it is unreachable until
   authoring exposes projectile detonation, so it is not a silent no-op — focus items 5, 7).
   (Threshold-reached = full detonation; carry-over not applicable under fizzle — document.)

## Ownership / data flow / phase (focus items 3, 4, 10)
- **Single writer:** this is the *only* system that mutates `TargetStackStateComponent`.
  Collision (005) emits intent; expansion/apply materialize detonations. No other writer.
- **No produce-and-consume of its own data:** it owns the target-stack lifecycle (tick →
  accumulate → detonate → clear) but emits detonations to the *separate* AOE spawn-event
  channel and never reads them back. The owned data (stack buffer) and the output data
  (spawn events) are distinct — this avoids the "one system produces, transforms, consumes,
  and cleans up the same data" smell.
- **Order:** `[UpdateAfter]` applicator collision systems, `[UpdateBefore(AoeSpawnExpansionSystem)]`.
  Lifetime tick runs each frame regardless of incoming events.

## Sequencing
Atomic with 002, 003, 005 (shared `StackApplyEvent` shape + buffer). Not independently green.

## Acceptance criteria
- Threshold reached → one detonation with summed contributions; entry cleared.
- Lifetime lapses below threshold → entry removed, no detonation.
- No tail/chain forwarding remains.

## Dependencies
003.

## Scope
Medium–large (the hot aggregation system; lifetime tick added).

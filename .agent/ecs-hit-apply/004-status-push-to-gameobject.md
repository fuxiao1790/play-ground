# 004 — Push status state to the GameObject

**Change:** add · **Depends:** 003 · **Scope:** small

## Goal

Surface the ECS status accumulator to the GameObject at the end of the frame,
through the same managed push the HP path uses — so the GameObject can reflect
stacks (UI/VFX/status icons) without owning accumulation.

## Changes

1. **`ICombatTarget` status callback** — add a compact, per-target status push
   parallel to `ReceiveHits`
   ([ICombatTarget.cs:55-64](../../Assets/Scripts/System/Common/ICombatTarget.cs#L55)):
   ```csharp
   void ReceiveStatus(IReadOnlyList<StatusStackSnapshot> stacks) { } // default no-op
   ```
   with `StatusStackSnapshot { int DebuffKey; int Count; float LifetimeRemaining; }`.
   Default-implemented so only targets that care (e.g. `MobRoot`) override it.

2. **Freeze the snapshot** — in `HitApplyFinalizeSystem`, after accrual, capture
   each touched target's current `TargetStackEntry` summary into a frozen
   per-target array (reuse the `TargetHitRange` grouping). Only push for targets
   whose stacks changed this frame (avoid pushing every target every frame).

3. **Push in the bridge** — in `HitApplyBridge`, after `ReceiveHits`, call
   `target.ReceiveStatus(...)` for targets with a status snapshot. Same resolve /
   `IsTargetUsable` guards as the HP push.

4. **Consume on `MobRoot`** (and player if relevant) — implement `ReceiveStatus`
   to update whatever the GameObject shows. Keep it read-only mirror; the
   GameObject must not write back into the ECS accumulator.

## Acceptance criteria

- Targets receive status snapshots at presentation time, same frame as the HP
  push, via the managed boundary (no ECS read of GameObject status).
- Targets with no status change in a frame receive no `ReceiveStatus` call.
- Snapshot count/lifetime matches the ECS accumulator post-accrual,
  pre-`StatusProcess` reduction (decide and document whether the GameObject sees
  pre- or post-detonation state for the frame).

## Notes / risks

- Decide the snapshot timing relative to `StatusProcessSystem`: simplest is
  pre-reduction (what was accrued this frame). If the GameObject needs to see a
  detonation having consumed a stack, push post-`StatusProcess` — but that
  reorders the freeze. Default: pre-reduction snapshot, documented.
- Keep `StatusStackSnapshot` blittable/small; this is the only new managed
  boundary added by the whole plan.

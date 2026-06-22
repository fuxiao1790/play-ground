# 004 — GameObject consumes the status half of ReceiveCombat

**Change:** add · **Depends:** 003 · **Scope:** small

## Goal

The combined push (`ReceiveCombat`) and the status snapshot it carries already
exist after 001 + 003. This task is the **managed side only**: have the
GameObjects actually reflect the status snapshot. No ECS changes.

## Changes

1. **`MobRoot.ReceiveCombat`** (and player if relevant) — override the default
   ([ICombatTarget.cs](../../Assets/Scripts/System/Common/ICombatTarget.cs),
   `ReceiveCombat` added in 001) so the `stacks` argument updates whatever the
   GameObject shows (status icons / `StatusEffects` / blackboard / UI). The HP
   half continues through the existing `ReceiveHit` → `TakeDamage` path
   ([MobRoot.cs:273-288](../../Assets/Scripts/Mob/MobRoot.cs#L273)).

2. **Read-only mirror** — the GameObject must not write back into the ECS
   accumulator; it only reflects the snapshot. Accrual authority stays in the
   finalize job.

## Acceptance criteria

- Targets reflect stacks at presentation time, same frame and **same call** as the
  HP application (one `ReceiveCombat`, not a second pass).
- Targets with no status change in a frame receive an empty `stacks` list (no
  separate call, no per-frame spam).
- Snapshot count/lifetime matches the ECS accumulator post-accrual,
  pre-`StatusProcess` reduction (the documented timing from 003).

## Notes / risks

- If a GameObject needs to see a detonation having consumed a stack within the
  frame, that requires a post-`StatusProcess` snapshot, which reorders the freeze.
  Default stays pre-reduction (what was accrued this frame); revisit only if a
  consumer needs it.
- `StatusStackSnapshot` stays blittable/small; `ReceiveCombat` is the only managed
  boundary the whole plan adds.

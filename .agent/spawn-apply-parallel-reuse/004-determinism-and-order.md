# 004 Determinism And Reuse/Cold-Create Order Safety

## Goal

Confirm that parallel apply plus lossy reuse (variable reuse/cold-create split and
different chunk fill order) does not change simulation results.

## Why This Matters

- Per-entity behavior is deterministic: reuse and cold-create write identical
  component values from the same command, and deterministic IDs/jitter come from
  command data, not slot identity.
- The only risk is a downstream consumer whose output depends on the order or
  chunk placement of spawned entities, since parallel apply and lossy reuse change
  which slot (and chunk) a given command lands in from frame to frame.

## Scope

- Audit consumers of freshly spawned entities for order dependence:
  - hit application / finalize path (`CombatApplyFinalize*`) — confirm hits are
    aggregated deterministically (e.g., target-bucketed) and not sensitive to
    producer entity order.
  - VFX dispatch — confirm queue draining/rendering is order-independent or
    already sorted.
  - Any float summation that accumulates across entities in iteration order.
- Confirm no system relies on newly created entities appearing in a specific chunk
  before the next structural sync.
- If an order dependence exists, decide: sort at the consumer, or constrain apply
  ordering for that path only. Do not reintroduce a serial claim cursor.

## Acceptance Criteria

- A short written finding: which consumers were checked and why each is
  order-independent (or the mitigation applied).
- A deterministic replay/sim test (existing or new) passes with the parallel apply
  under repeated runs and under forced overflow (small pool, many commands).

## Dependencies

Depends on 002 and 003.

## Complexity

Small to medium.

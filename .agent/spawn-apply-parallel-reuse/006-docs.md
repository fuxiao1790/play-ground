# 006 Docs

## Goal

Document the parallel apply pipeline and the accepted lossy-reuse tradeoff.

## Scope

- Update spawn flow/reference docs:
  - `Docs/flows/spawn-event-to-entity.md` — event queue → command handoff →
    parallel dead-slot apply → reuse + ECB remainder.
  - `Docs/reference/simulation/projectile-system.md`,
    `Docs/reference/simulation/aoe-system.md` — apply is one parallel job per
    domain; no shared claim cursor.
  - `Docs/profiling.md` — note the apply parallelization and how to read the
    reuse/cold counters.
- State the lossy-reuse tradeoff explicitly: uneven reuse is intentional; the
  resident pool converges to a slightly larger steady-state size; overflow
  cold-creates are the cost, bounded and self-limiting.
- Note the optional per-chunk free-slot popcount as a documented future lever, not
  a current behavior.
- Update ECS lifecycle/notes comments if they describe serial reuse.

## Acceptance Criteria

- Docs describe the two-hop pipeline and parallel per-domain apply.
- Docs state the accepted lossy-reuse tradeoff and pool convergence.
- No doc still describes a single shared claim cursor as the reuse mechanism.

## Dependencies

Depends on 001 through 005.

## Complexity

Small.

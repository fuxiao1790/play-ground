# 005 — Docs and memory

## Goal
Record the trimmer in the simulation/perf docs and in agent memory so the retained-
pool behavior and its bounded reclamation are discoverable.

## Docs to update
- `Docs/reference/simulation/ecs-notes.md` — add the disable-in-place pool lifecycle:
  expire→disable→reuse→bounded end-of-sim trim. State the invariant that the trimmer
  runs in `LateSimulationSystemGroup` after spawn-apply and never deletes below the
  per-batch floor.
- `Docs/performance.md` — note the idle-cost origin (retained chunks after a heavy
  scene) and how the trimmer caps it; link back to the profiling finding.
- `Docs/reference/simulation/index.md` — one-line pointer to the new system, if it
  lists systems.

## Memory
Add/refresh a `project` memory entry:
- name: `project_pool_cleanup`
- description: bounded end-of-sim trimmer for the disable-in-place combat pool;
  plan in `.agent/pool-cleanup/`; both-gate headroom + per-batch retention/ratio.
- Link `[[project_idle_combat_system_cost]]` (the non-shrinking pool it addresses) and
  the render idle-cost finding.
Update `MEMORY.md` index with the one-line pointer.

## Acceptance criteria
- Docs describe the full pool lifecycle including trimming and its guarantees.
- Memory entry + index line exist.

## Dependencies
- Lands after 003 (behavior finalized).

## Scope
Small — docs + one memory file.

# 010 — Documentation Update

## Goal
Keep `.agent/vfx-graph-agent.md` (the actual operating guide an agent reads
before touching the bridge — see `Command Reference`/`Workflow` sections) in
sync with the new capability. This is the highest-leverage doc change: it's
what stops a future agent from hitting the same "Not available" wall this
whole plan exists to remove.

## Dependencies
001-009 (documents the finished feature; should reflect what actually got
built, including any deviations from this plan noted in
`implementation-log.md`).

## Files to change
- `.agent/vfx-graph-agent.md`:
  - **Command Reference** table: add `vfx_internal_find_types`,
    `vfx_internal_describe_type`, `vfx_internal_describe_object`,
    `vfx_internal_exec` rows.
  - **Known Capabilities and Limitations**: this section needs real
    correction, not just addition. For each of the four documented gaps
    (top-level node creation, flow wiring, `AnimationCurve` slots,
    engine-reference-typed slots) — if task 009's tests prove the executor
    closes it, move it from "Not available" to "Available" with a one-line
    note on *how* (e.g. "via `vfx_internal_exec` `call` on
    `VFXContext.LinkFrom`/`LinkTo`" — use whatever the actual implemented
    op sequence turned out to be, not a guess). If a gap turns out NOT
    closeable by the executor alone (candidate for task 011), leave it
    documented as still-blocked and say why.
  - **Workflow** section: extend step 2 ("Discover, don't assume") to
    mention `vfx_internal_find_types`/`describe_type` as the path for
    anything not in `vfx_types_list`. Extend step 4 ("Mutate") to describe
    when to reach for `vfx_internal_exec` per the guide's own policy (guide
    §17: convenience commands first, exec only when they don't fully
    express the edit).
  - Add a short new subsection (or fold into Workflow) mirroring guide §14's
    caveat as documented in task 006: `vfx_internal_exec` failure partway
    through a multi-op sequence does NOT auto-rollback earlier operations
    in that same call — always `vfx_graph_read`/`vfx_errors` after a failed
    exec call to see actual resulting state.
  - Update the **Before Doing Anything: Confirm Connectivity** section only
    if command names/behavior there changed (expected: no change needed,
    `vfx_ping` behavior is untouched).
- No change expected to `Docs/` (the Unity-project-wide doc set) — this
  bridge's design record lives entirely under `.agent/`, per the existing
  convention (`.agent/vfx-graph-agent.md` itself, referenced from nowhere in
  `Docs/`).

## Acceptance Criteria
- Every new command is documented with the same level of detail (args,
  return shape, one-line semantic note) as existing rows.
- "Known Capabilities and Limitations" accurately reflects task 009's actual
  test results — this task must be done (or at least finalized) AFTER 009,
  not written speculatively ahead of what got proven.

## Scope
Small, but must not be rushed — this file is what prevents the next agent
session from re-discovering the same gaps from scratch.

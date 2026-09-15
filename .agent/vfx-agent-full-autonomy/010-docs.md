# 010 - Operating Guide and Evidence Update

## Goal

Update `.agent/vfx-graph-agent.md` to match implemented and user-verified
behavior. Plan completion does not itself prove new capabilities.

## Dependencies

001-009 code complete, then user-provided
`Logs/TestResults-EditMode-VfxAgent.xml` reviewed.

## Required Changes

- Add four new command rows with exact args/result shapes.
- Document `graph` sentinel for top-level `vfx_node_create`.
- Document tagged curve, gradient, asset, enum, type, local-ref, and durable-ref
  value shapes.
- Update discovery workflow: convenience catalogue first; bounded internal
  introspection only when fixed type description is insufficient.
- Update mutation workflow: convenience commands first; executor only for
  graph-local operations not otherwise expressible.
- State executor batch semantics plainly: sequential, no rollback, no save on
  failure, `mayHaveMutated` requires immediate graph/error reread.
- State `saveOnSuccess` default and retain compile/verify requirements.
- Update graph-read contract for `slots`, `flowConnections`, settings,
  `childIndex`, runtime types, and `dataId`.
- Remove stale warning against bridge use only for capabilities proven by XML.
  Keep failed/uncovered cases under “Not available,” with exact reason.
- Remove stale mojibake/encoding artifacts only if file bytes actually contain
  them; terminal rendering alone is not evidence of corrupt source.

## Acceptance Criteria

- Every documented command and JSON example matches source signatures.
- No claim says executor is atomic, supports arbitrary Unity reflection, or can
  execute static/editor/file APIs.
- Each moved capability maps to a named passing test in reviewed XML.
- Connectivity and scratch-copy safety guidance remains intact.

## Scope

Small, evidence-gated.

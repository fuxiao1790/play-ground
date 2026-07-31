# 008 — Documentation Update

## Change

Update the docs that currently describe target-proxy push as direct/synchronous to
describe the event-based flow instead:

- `Docs/contracts/target-proxy.md` — "Lifetime" section currently says "Actor
  registration creates the proxy and seeds Health/Mana..." and "Roots push Max/regen
  changes... Actor updates also push shape/position" as if these are immediate. Update
  to describe the event → apply split and note the one-tick resolution delay after
  registration.
- `Docs/flows/target-proxy-lifecycle.md` — "Sequence" step 3-5 currently reads as
  synchronous (registry creates proxy, actor stores handle, actor pushes data). Update
  to reflect: registry enqueues a create event; actor's proxy handle resolves on a
  later tick; subsequent pushes are themselves events applied on a later tick.
- `Docs/flows/runtime-frame.md` — step 2 ("Player and mob roots push target proxy
  position and shape") and step 12 ("Actor roots delete queued invalid target
  proxies") should note these are now event enqueues consumed by
  `TargetProxyCreateApplySystem`/`TargetProxyUpdateApplySystem`/`TargetProxyDeleteApplySystem`
  at defined points in `SimulationSystemGroup`/`PresentationSystemGroup`, not direct
  writes.
- Consider adding a short "Target Proxy Event Pipeline" note alongside the existing
  spawn-pipeline references in `Docs/contracts/spawn-events-and-commands.md` or a new
  small contract doc, so the two event pipelines (spawn, target-proxy) are
  cross-referenced as siblings rather than the target-proxy one reading as a surprising
  one-off.

## Acceptance Criteria

- No doc describes target-proxy push/create/delete as synchronous or "immediate"
  anymore.
- The open TODOs already present (`runtime-frame.md:69`, `target-proxy-lifecycle.md:61`
  re: `LateUpdate()` ordering) are preserved, not silently resolved — this plan
  explicitly does not resolve them (see index.md Open Questions).

## Dependencies

Depends on 001-007 (docs should describe the final, implemented state, not the
in-progress design).

## Scope

Small. Prose edits to three existing docs, no code.

# 005 — Explicit VFX event/memory budget (DEFERRED)

## Status: accepted debt; do not block 002

Per user decision, 002 ships chunked high-water growth without an application-level event or byte
budget. Endpoint capacity therefore depends on the maximum number of events routed to that endpoint
in any one tick and remains allocated for the endpoint's lifetime.

This task records the required follow-up. It is not part of the current implementation sequence.

## Goal

Replace allocation failure as the only fallback with an intentional presentation budget that is
independent of `GrowthChunk`. The policy must make retained GPU/native memory predictable while
keeping VFX visual-only and preserving gameplay authority.

## Required decisions before implementation

- Choose per-endpoint, global, or combined event/byte limits from profile captures and authored
  graph capacities.
- Define which events are skipped when demand exceeds the budget; do not rely on queue ordering,
  because producer order is nondeterministic.
- Define whether a rare high-water allocation may decay or shrink at a safe lifecycle boundary.
- Add accepted/dropped counts and high-water bytes/events to diagnostics before content stress.
- Keep growth granularity separate from policy: 2048 may remain an allocation chunk but must never
  silently become the semantic spawn budget again.

## Acceptance criteria

- Maximum retained native and GPU bytes are predictable from explicit configuration.
- Overflow follows a named, documented policy and increments visible diagnostics.
- Budget enforcement causes no gameplay changes and no per-frame managed allocation.
- Stress tests cover sustained overload, one-tick spikes, and recovery after demand falls.

## Dependencies

Depends on 002 and real profile captures from representative dense content. Deferred until the
current endpoint-routing work is validated.

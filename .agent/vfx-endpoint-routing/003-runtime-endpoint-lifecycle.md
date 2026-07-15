# 003 — Runtime endpoint creation and staged removal

## Problem

Endpoints only exist if `SkillDriver.CompileAndRegister` registered them at startup
(`SkillDriver.cs:58`, `:122`, `:627-637`). There is no supported way to add a graph mid-session,
and no removal path at all — `CombatAoeVfxDispatcher.Dispose()` tears down everything at once
(`:247-255`), and `CombatVfxRoot.ResetDispatcher()` (`:48-52`) is a blunt rebuild-the-world hammer.

Consequences:

- a skill/graph introduced at runtime cannot get a visual until a full dispatcher reset;
- an endpoint whose asset is no longer referenced holds its GameObject and GPU buffers for the
  session's lifetime;
- `ResetDispatcher` destroys and rebuilds live endpoints, which is why it is only safe outside a
  drain.

## Change

Adopt the one genuinely good structural idea from the reviewed proposal: **the endpoint is a
stable identity; its resources are swappable behind it.** Implemented as a dense int + record,
not an entity (see index rationale).

1. **`endpointId` is stable and never reused within a session.** `endpoints` stays dense;
   removed slots hold a tombstone (`null`) rather than compacting, so no live `endpointId`
   changes meaning. `endpointIdByKey` entries pointing at a tombstone resolve to "no visual".
2. **Runtime creation and routing**: expose
   `int EnsureRoute(int typeId, AoeVfxTrigger trigger, VisualEffectAsset asset,
   bool requireAreaSizeContract = false)` on `CombatVfxRoot`. It validates this route's requested
   contract, idempotently resolves or creates the asset endpoint, binds the `(typeId, trigger)`
   route, and returns its `endpointId`. The existing `Register(...)` API becomes a compatibility
   wrapper over `EnsureRoute`, so startup and runtime registration use one complete path.

   Keep `EnsureEndpoint(asset, requiredContract)` as a private dispatcher helper only. Endpoint
   creation by itself is not a public success state because requests carry route keys, not endpoint
   ids. First-registration-wins for an already-bound route remains the 001 behavior: `EnsureRoute`
   returns that route's existing endpoint id and does not create or remap the newly supplied asset.
3. **Staged removal**: `RemoveEndpoint(int endpointId)` must not free resources while a drain
   could still stage into them. Order:
   - drop every `endpointIdByKey` entry resolving to it (new requests stop routing there);
   - flush or discard its staged events;
   - tombstone the slot and remove from `endpointByAsset` + `LiveResources`;
   - dispose staging lists, release buffers, destroy the GameObject.
   `RemoveEndpoint` is **main-thread only** and queues the id in a managed pending-removal list; it
   does not release resources immediately. `CombatAoeVfxDispatchSystem.OnUpdate` calls
   `CombatVfxRoot.ApplyPendingRemovals()` immediately after `ProducerHandle.Complete()` and before
   both the queue-empty early return and `DrainAndDispatch`. This guarantees removal progresses on
   quiet frames and cannot run during the drain. `OnDestroy` may dispose all remaining endpoints
   immediately after dispatch ownership has ended.
4. **Resource swap**: because callers hold only an `endpointId`, an endpoint's buffers/list/
   `VisualEffect` can be replaced without touching any caller. 002's growth is already an instance
   of this; keep the record's internals private so that stays true.
5. `ResetDispatcher()` is retained but re-expressed as "remove all endpoints" using the staged
   path, so it stops being a special case with its own teardown semantics.

## Invariants respected

- **Native/GPU handle ownership** (`Docs/coding-standards.md:292-313`): removal disposes staging
  and releases buffers on every path; tombstones must not leak. Full `Dispose()` must skip
  tombstones without double-releasing.
- **Hot path** (`:282`): `EnsureRoute` and its private `EnsureEndpoint` helper are setup work, not
  per-frame. Do not call either from the drain. This is also the existing rule that expensive setup
  must not hide inside repeated runtime calls (`Docs/coding-standards.md:113-115`).
- **No structural ECS changes**: endpoints are presentation-side records; no ECS component is
  added or removed on any combat entity.

## Explicitly not in this task

- No ECS endpoint entity. Rejected in the index — an entity is only needed if Burst jobs write
  per-endpoint containers, which this plan does not do.
- No reference counting of endpoints by slot. Deferred: nothing today unregisters a slot, so a
  refcount would be untested speculation. `RemoveEndpoint` is explicit-call-only for now.

## Acceptance criteria

- `EnsureRoute` called twice for routes using the same asset returns the same endpoint id and
  allocates once.
- A route registered mid-session receives matching `(typeId, trigger)` events on the next dispatch
  cycle with no reset.
- `RemoveEndpoint` frees the GameObject and both buffers; the child disappears from under
  `CombatVfxRoot`; no leaked GPU handles across repeated add/remove cycles.
- Requests routed to a removed endpoint are discarded cleanly (`StageAoeSpawn` returns `false`),
  never dereference a tombstone, and never throw.
- Removal invoked during a frame with pending staged events does not corrupt or crash the drain.
- `ResetDispatcher` still yields a working dispatcher and leaks nothing.
- Startup registration behavior is byte-for-byte unchanged from 001's outcome.

## Verification

Harness cannot run Unity. User-run play-mode verification:

1. Enter play mode; confirm startup-registered effects behave exactly as after 001.
2. Call `EnsureRoute` at runtime with a new route and asset; enqueue a matching request and confirm
   the child appears and the effect fires without a dispatcher reset.
3. Call `RemoveEndpoint` on a live endpoint mid-combat; confirm the child disappears, no
   exception fires, and other effects keep working.
4. Repeat add/remove ~50 times; confirm child count and GPU memory return to baseline.

## Dependencies

**Depends on 001** (endpoint identity and the asset map must exist first). Independent of 002,
but 002 should land first so growth and lifecycle are not rewritten against each other.

## Scope

Medium. `CombatAoeVfxDispatcher` + `CombatVfxRoot` + a pending-removal hook in
`CombatAoeVfxDispatchSystem.OnUpdate`. Still no producers, no jobs, no ECS components.

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
2. **Runtime creation**: expose `int EnsureEndpoint(VisualEffectAsset asset)` on
   `CombatVfxRoot`. Idempotent — returns the existing `endpointId` if the asset already has one,
   otherwise builds it (validate contract, GameObject, buffers, staging) and returns the new id.
   `Register(typeId, trigger, asset)` becomes `EnsureEndpoint` + `endpointIdByKey[key] = id`.
   Startup registration therefore goes through the same path as runtime registration — one code
   path, not two.
3. **Staged removal**: `RemoveEndpoint(int endpointId)` must not free resources while a drain
   could still stage into them. Order:
   - drop every `endpointIdByKey` entry resolving to it (new requests stop routing there);
   - flush or discard its staged events;
   - tombstone the slot and remove from `endpointByAsset` + `LiveResources`;
   - dispose staging lists, release buffers, destroy the GameObject.
   Removal is **main-thread only and must not run between `ProducerHandle.Complete()` and the end
   of `DrainAndDispatch`**. Simplest safe placement: a pending-removal list applied at the top of
   `CombatAoeVfxDispatchSystem.OnUpdate`, before the drain.
4. **Resource swap**: because callers hold only an `endpointId`, an endpoint's buffers/list/
   `VisualEffect` can be replaced without touching any caller. 002's growth is already an instance
   of this; keep the record's internals private so that stays true.
5. `ResetDispatcher()` is retained but re-expressed as "remove all endpoints" using the staged
   path, so it stops being a special case with its own teardown semantics.

## Invariants respected

- **Native/GPU handle ownership** (`Docs/coding-standards.md:292-313`): removal disposes staging
  and releases buffers on every path; tombstones must not leak. Full `Dispose()` must skip
  tombstones without double-releasing.
- **Hot path** (`:282`): `EnsureEndpoint` is setup work, not per-frame. Do not call it from the
  drain. This is also the existing rule that expensive setup must not hide inside repeated runtime
  calls (`Docs/coding-standards.md:113-115`).
- **No structural ECS changes**: endpoints are presentation-side records; no ECS component is
  added or removed on any combat entity.

## Explicitly not in this task

- No ECS endpoint entity. Rejected in the index — an entity is only needed if Burst jobs write
  per-endpoint containers, which this plan does not do.
- No reference counting of endpoints by slot. Deferred: nothing today unregisters a slot, so a
  refcount would be untested speculation. `RemoveEndpoint` is explicit-call-only for now.

## Acceptance criteria

- `EnsureEndpoint` called twice with the same asset returns the same id and allocates once.
- A graph registered mid-session receives events on the next dispatch cycle with no reset.
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
2. Call `EnsureEndpoint` at runtime with a new asset; confirm the child appears and the effect
   fires without a dispatcher reset.
3. Call `RemoveEndpoint` on a live endpoint mid-combat; confirm the child disappears, no
   exception fires, and other effects keep working.
4. Repeat add/remove ~50 times; confirm child count and GPU memory return to baseline.

## Dependencies

**Depends on 001** (endpoint identity and the asset map must exist first). Independent of 002,
but 002 should land first so growth and lifecycle are not rewritten against each other.

## Scope

Medium. `CombatAoeVfxDispatcher` + `CombatVfxRoot` + a pending-removal hook in
`CombatAoeVfxDispatchSystem.OnUpdate`. Still no producers, no jobs, no ECS components.

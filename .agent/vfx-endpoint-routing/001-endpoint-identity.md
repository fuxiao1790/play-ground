# 001 — Endpoint identity keyed by VisualEffectAsset

## Problem

`CombatAoeVfxDispatcher` keys its resource dictionary by `(typeId, trigger)`
(`CombatAoeVfxDispatcher.cs:181`):

```csharp
private static int KeyFor(int typeId, AoeVfxTrigger trigger)
    => typeId * TriggerKeyStride + (byte)trigger;
```

Every distinct key mints its own `GameObject` + `VisualEffect` + `PositionBuffer` +
`AreaSizeBuffer` + two staging lists (`:91-124`) — even when the underlying
`VisualEffectAsset` is identical. `SkillDriver` registers up to 5 slots per AOE type
(`SkillDriver.cs:632-636`), so one asset reused across trigger slots or AOE types yields N
duplicate children running the same graph.

Neither `typeId` nor `trigger` reaches the GPU: `StageAoeSpawn` consumes only `Position` +
`AreaSize` (`:163-179`), and `Dispatch()` uses constant property/event names (`:186-208`).

## Change

Make the **asset** the identity. `(typeId, trigger)` becomes a routing key that resolves to a
dense `endpointId`.

1. Rename `AoeVfxTypeResources` -> `AoeVfxEndpoint` (it is now per-graph, not per-type). Keep
   its fields; `MaxPerFrame` stays untouched in this task (002 replaces it).
2. In `CombatAoeVfxDispatcher`, replace `Dictionary<int, AoeVfxTypeResources> resources` with:
   - `List<AoeVfxEndpoint> endpoints` — dense, indexed by `endpointId`.
   - `Dictionary<VisualEffectAsset, int> endpointByAsset` — asset identity -> `endpointId`.
   - `Dictionary<int, int> endpointIdByKey` — `KeyFor(typeId, trigger)` -> `endpointId`.
     Absent = no visual for that slot.
3. `Register(typeId, trigger, asset, ...)`:
   - `asset == null` -> return (unchanged: no visual, no allocation).
   - If `endpointIdByKey` already contains `key` -> return before inspecting `asset`. This
     preserves today's first-registration-wins behavior and prevents a repeated compile from
     silently rerouting a live slot or orphaning its previous endpoint.
   - If `endpointByAsset` already has `asset` -> **do not** create a GameObject or buffers;
     validate the asset against this call's requested contract, then map
     `endpointIdByKey[key] = existingId` and return. A stronger later contract must not inherit a
     weaker first registration without validation.
   - Otherwise create the endpoint exactly as today (GameObject, validate contract, buffers,
     staging), append to `endpoints`, and record both maps.
   - Keep the existing try/catch partial-alloc rollback (`:126-137`) and the `LiveResources`
     bookkeeping.
   - Name the GameObject after the **asset** (e.g. `$"Vfx_{asset.name}"`), not
     `$"Vfx_{typeId}_{trigger}"` — the old name is now a lie when shared.
4. `StageAoeSpawn(typeId, trigger, position, areaSize)`:
   - resolve `endpointIdByKey[KeyFor(...)]`; miss -> return `false` (unchanged semantics);
   - stage into `endpoints[endpointId]`.
5. `Dispatch()` iterates `endpoints` instead of `resources.Values`. Unchanged otherwise.
6. `Dispose()` disposes each endpoint once (the dense list guarantees no double-dispose, which
   a naive alias over the old dictionary would have risked).

## Explicitly not in this task

- No change to `AoeVfxSpawnRequest`, the queue, `ProducerHandle`, or any producer system.
- No change to `maxPerFrame` behavior (002).
- No `endpointId` in the request payload and no ECS-side table yet (004 adds one only if needed).
- The 001-003 main-thread route lookup is deliberately the managed `Dictionary<int, int>` above.
  A native flat lookup table is only an optional cache introduced by measurement-gated 004.
- No shared components, no new ECS components, no structural changes anywhere.

## Conflict note

If two slots register the **same asset** with different `maxPerFrame`, the endpoint is shared and
the first capacity wins. Today every call site passes the default `2048`. Log a warning if a later
registration requests a different value; 002 removes the field and ambiguity entirely.

`requireAreaSizeContract` does **not** use first-endpoint-creation-wins. Every newly bound route
must validate the asset against its own requested contract, including when the endpoint already
exists. If a later route requires `AreaSizes` and the shared asset does not expose it, that route
remains unmapped and logs the normal validation error; existing weaker routes remain valid. A
repeated call for an already-bound key remains a no-op under first-registration-wins.

## Acceptance criteria

- Registering one asset across N `(typeId, trigger)` slots produces **exactly one** child
  GameObject under `CombatVfxRoot`, not N.
- Child count under the VFX root equals the number of **distinct** `VisualEffectAsset`s that
  registered successfully.
- Requests routed via any of the N slots all reach that one endpoint and appear in one batch —
  a request from `(typeA, Hit)` and one from `(typeB, Pulse)` sharing an asset land in the same
  `Positions` upload and one `SendEvent`.
- Slots with a `null` asset still allocate nothing and still cause `StageAoeSpawn` to return
  `false`.
- Re-registering an existing `(typeId, trigger)` with a different asset leaves the original route
  unchanged and does not allocate an orphan endpoint.
- Reusing an endpoint never bypasses a later route's stronger graph-contract validation.
- A graph failing `ValidateGraphContract` still logs, destroys its GameObject, and registers no
  endpoint; other slots pointing at that asset resolve to no endpoint.
- No duplicate `Dispose` / double `Release` on teardown when an asset is shared by many slots.
- Existing AOE play-mode tests pass unchanged.

## Verification

Harness cannot run Unity. User-run play-mode verification:

1. Enter play mode on a scene with AOE skills whose slots reuse one asset.
2. Inspect children under `CombatVfxRoot` — count must equal distinct assets in use.
3. Confirm effects still fire for every trigger (spawn, hit, expire, pulse, arming) that has an
   asset assigned.
4. Confirm `CombatStatsSingleton.VfxEventsCreated` still increments (the accepted-count return
   path is unchanged).

## Dependencies

None. This is the first task and is independently shippable.

## Scope

Small–medium. One managed class (`CombatAoeVfxDispatcher`), plus a rename touching
`CombatVfxRoot` only through existing method signatures. No ECS, no jobs, no producers.

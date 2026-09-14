# 005 — Executor Dispatcher + Read-Only Ops

## Goal
`vfx_internal_exec`'s operation dispatcher and the read-only operation set
(guide §5-13, Step 3 of the minimal implementation order):
`get_graph`, `resolve_type`, `get`, `call`, `call_static`, `enumerate`,
`index`, `cast`. Mutation ops (`set`, `create`, `create_scriptable_object`,
`mark_dirty`, `save`) and the transactional wrapper are task 006 — split so
this is independently reviewable and testable before mutation risk is added.

## Dependencies
001, 002, 003 (value codec), 004 (shares the type-resolution helper with
`resolve_type`).

## Files to create
- `Assets/AgentVFX/InternalAccess/AgentVfxExecOps.cs`
  (`static partial class AgentVfxInternalBridge`):
  - Public entry: `Exec(string assetPath, string operationsJson) → string`
    (bare JSON text in/out — index.md invariant 6/Design Validation: the
    executor's shape is inherently dynamic, so it stays a JSON string at the
    `AgentVfxApi`/`AgentVfxInternalBridge` boundary rather than a typed DTO).
    For this task, treat the method as read-only-safe: no Undo group, no
    `mark_dirty`/`save` handling yet (task 006 adds the transactional
    wrapper around the same dispatcher loop). Do not call `save` on the
    graph in this task even if an op sequence would otherwise leave the
    graph dirty — that's explicitly a task 006 concern.
  - Request shape per guide §6: `{"asset": "...", "operations": [{"op": "...", "as": "...", ...op-specific fields}, ...]}`.
  - A per-request `Dictionary<string, object> handles` (the `$myName` ->
    value bindings from `"as"`, guide §15) — scoped to one `Exec` call, reset
    each call. This is **separate** from `AgentVfxHandleMap`/`AgentVfxIdMap`:
    `$myName` bindings are local aliases within one operations array, while
    `AgentVfxHandleMap`/`AgentVfxIdMap` ids are what actually gets returned
    to the agent and persist across calls. An op result gets BOTH: stored
    under its local `$myName` if `"as"` was given (for later ops in the same
    call to reference via `"target": "$myName"`), AND encoded via
    `AgentVfxExecValueCodec.Encode` for the response payload (which is what
    produces the durable `node:`/`slot:`/`obj:` id an agent can reuse next
    call). Keep these two concerns distinct in the code — a local-alias
    lookup table and the codec's handle encoding are different jobs.
  - Ops for this task:
    - `get_graph` — `{"asset": "..."}` (or read the request-level `asset`)
      → `OpenGraph(assetPath)`, store as the op's handle.
    - `resolve_type` — `{"type": "Full.Name"}` → `Type`, using the shared
      resolver from task 004.
    - `get` — `{"target": "$x", "member": "name", "memberKind": "field"|"property"|null}`
      → reflect field/property (public+non-public), read value, encode.
    - `call` — `{"target": "$x", "method": "Name", "parameterTypes": [...], "args": [...]}`
      → resolve the exact overload by parameter types (required whenever
      more than one overload matches by name — guide §11), decode args via
      the codec against each resolved parameter's real type, invoke,
      encode result.
    - `call_static` — same as `call` but `"type"` instead of `"target"`.
    - `enumerate` — `{"target": "$x"}` → if `IEnumerable`, yield each
      element encoded as a handle (guide §12 — complex elements come back as
      handles, not inlined values).
    - `index` — `{"target": "$x", "index": N}` → `IList`/array indexer.
    - `cast` — `{"target": "$x", "type": "Full.Name"}` → runtime type check +
      rebind the handle's declared type for subsequent `get`/`call` member
      resolution (needed when a member is only visible through a base/
      interface type at the reflection level).
  - Structured failure (guide §14, first half — full transactional guarantee
    is task 006): if any operation throws, stop the loop and return
    `{"success": false, "failedOperation": <index>, "error": {"type": "...", "message": "...", "stackTrace": "..."}}`
    — catch the exception here, do not let it propagate out of `Exec` as a
    raw CLR exception (that would surface as an opaque CLI transport error
    instead of the structured shape the guide asks for).

## Acceptance Criteria
- A 2-3 op sequence (`get_graph` → `get` `children` → `enumerate`) against a
  real graph returns handles for each child, and each returned handle for a
  child that's actually a `VFXModel` matches what `vfx_graph_read` would
  have assigned it (id-map-reuse invariant — verify by cross-checking
  against `AgentVfxApi.ReadGraph` in the same test).
- `call` with an ambiguous method name and explicit `parameterTypes` resolves
  the correct overload (test against a real multi-overload `UnityEditor.VFX`
  method found via `vfx_internal_describe_type` during implementation).
- An op that throws (e.g. `get` on a nonexistent member name) produces the
  structured failure shape, not an unhandled exception.

## Scope
Large — this is the core of the whole feature. Expect this to be the biggest
single-file task in the plan.

# 002 — AgentVfxHandleMap

## Goal
General-purpose weak handle registry for objects the executor produces that
are NOT `VFXModel` (arrays/lists, `Type`, boxed structs, `VFXData`, etc.), so
`vfx_internal_exec` operations can chain via `$ref` and
`vfx_internal_describe_object` can look one back up later.

## Dependencies
001 (internal access must compile before anything here is exercised against
real VFX types, though this file itself has no VFX Graph dependency).

## Files to create
- `Assets/AgentVFX/InternalAccess/AgentVfxHandleMap.cs`
  - `internal static class AgentVfxHandleMap`, same shape as
    [AgentVfxIdMap.cs](../../Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs):
    `ConditionalWeakTable<object, IdBox>` + `Dictionary<string, WeakReference<object>>`.
  - Id format `obj:N` — must never collide with `node:N` / `slot:N` from
    `AgentVfxIdMap` (different prefix already guarantees this).
  - `GetOrCreateId(object value)`, `TryResolve(string id, out object value)`,
    `Resolve(string id)`, `Resolve<T>(string id)`.
  - Do **not** special-case `VFXModel` inside this map — callers (the value
    codec, task 003) are responsible for checking `is VFXModel` first and
    calling `AgentVfxIdMap` instead, so there is exactly one place that
    routing decision is made (see index.md "Design Validation" — Id map
    reuse).
  - Value types (structs) passed here get boxed by the `object` parameter;
    document that a boxed struct handle is a *copy* — mutating through a
    handle obtained this way (e.g. `set` on a field of a boxed struct) will
    not affect the original. Real VFX mutation targets are almost always
    reference types (`VFXModel`, `VFXSlot`, arrays, `List<T>`), so this is a
    documented limitation, not a blocking one.

## Acceptance Criteria
- Unit-test-shaped behavior (see task 009 for the actual test file): same
  object reference returns the same id on repeated `GetOrCreateId` calls;
  two different objects get different ids; `Resolve` on an unknown id throws
  `InvalidOperationException` with a message telling the caller to re-run
  the operation that produced the handle (mirrors
  [AgentVfxIdMap.cs:57](../../Assets/AgentVFX/InternalAccess/AgentVfxIdMap.cs#L57)'s
  "re-run vfx_graph_read" wording, adapted).

## Scope
Small, mechanical — mirrors an existing file closely.

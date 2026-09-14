# 003 — AgentVfxExecValueCodec

## Goal
Structured JSON <-> CLR value conversion for executor operation
arguments/results (guide §13), distinct from `AgentVfxJson` (which is
deliberately scoped to bare slot/setting literals only, per its own header
comment).

## Dependencies
001, 002 (needs `AgentVfxHandleMap` for `$ref` decode/encode).

## Files to create
- `Assets/AgentVFX/InternalAccess/AgentVfxExecValueCodec.cs`
  - `internal static class AgentVfxExecValueCodec` using
    `Newtonsoft.Json.Linq` (`JToken`/`JObject`/`JArray`) — confirmed
    available to this assembly without an asmdef change (index.md invariant 7).
  - `Decode(JToken token, Type expectedType)` — used when the target CLR
    type is known (a `set`/`call` argument matched against a reflected
    member/parameter type). Handles, per guide §13:
    - `null`, `bool`, `int`, `long`, `float`, `double`, `string` — direct.
    - `{"$enum": {"type": "...", "value": "..."}}` → `Enum.Parse`.
    - `{"$type": "..."}` → `Type.GetType`/`AppDomain` type resolution (share
      logic with `resolve_type`, task 005 — put the actual type-resolution
      helper here so both call it, don't duplicate it in `AgentVfxExecOps`).
    - `{"$ref": "obj17"}` / `{"$ref": "node:5"}` / `{"$ref": "slot:12"}` →
      `AgentVfxHandleMap.Resolve` or `AgentVfxIdMap.Resolve`, tried in that
      order based on the id's prefix (`node:`/`slot:` → `AgentVfxIdMap`,
      `obj:` → `AgentVfxHandleMap`) — prefix dispatch, not a try/catch
      fallback, so an unknown-prefix id fails clearly.
    - `{"$asset": "Assets/Textures/Test.png", "type": "UnityEngine.Texture2D"}`
      → `AssetDatabase.LoadAssetAtPath`. Per index.md Design Validation, this
      path is **not** run through `AgentVfxPathGuard` — it's a read of
      existing content, not a graph write target.
    - Vector2/3/4, Color, Rect → field-by-field from a `JObject` (mirror the
      field names `AgentVfxJson`/`JsonUtility` already use for these so a
      value round-trips identically whether it came from `vfx_slot_set` or
      `vfx_internal_exec`).
    - `JArray` → array/`List<T>` when `expectedType` is known (element type
      drives per-element `Decode`); when `expectedType` is `null`/`object`,
      decode into `object[]` with per-element type inferred from each
      element's own `$`-tag or JSON literal type.
  - `Encode(object value, Type declaredType = null)` — used for operation
    results (`get`, `call` return, `enumerate` elements):
    - `null`/primitives/`string` → JSON scalar.
    - `Enum` → `$enum` shape.
    - `Type` → `$type` shape.
    - `value is VFXModel` → `{"$ref": AgentVfxIdMap.GetOrCreateId(value)}`
      (checked **before** the generic reference-type fallback — this is the
      id-map-reuse invariant from index.md).
    - `value is UnityEngine.Object` (not a `VFXModel`, e.g. a `Texture2D`)
      → `$asset` shape via `AssetDatabase.GetAssetPath`.
    - Vector2/3/4, Color, Rect → same field shape as `Decode`.
    - Any other reference type or boxed struct → `{"$ref": AgentVfxHandleMap.GetOrCreateId(value)}`.
  - No case should silently drop information — an unrecognized CLR type
    still gets a `$ref` handle (never a bare `"{}"` or `null` fallback the
    way `AgentVfxJson.ToJson`'s default case does; that's fine for that
    file's narrower scope, wrong here since exec callers need to chain
    handles reliably).

## Acceptance Criteria
- Round-trip: `Decode(Encode(x))` reproduces an equivalent value for every
  listed primitive/struct kind.
- Encoding a `VFXModel` produces the *same* id `AgentVfxIdMap.GetOrCreateId`
  would have produced directly (interop check — write this as an explicit
  test in task 009: create a node via `AgentVfxApi.CreateNode`, then obtain
  its handle via an exec `get_graph`/`enumerate` chain, assert the ids
  match).
- Decoding an unknown `$ref` id throws with a clear message, not a null
  reference further down the call chain.

## Scope
Medium — the single most detail-heavy new file, but self-contained (no
`AgentVfxExecOps` dispatcher logic here, just value conversion).

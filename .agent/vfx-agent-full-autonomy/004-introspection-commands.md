# 004 — Introspection Commands

## Goal
`vfx_internal_find_types`, `vfx_internal_describe_type`,
`vfx_internal_describe_object` (guide §4) — let the agent discover the real
installed VFX Graph API before calling it through the executor, without
guessing member names/overloads from memory.

## Dependencies
001 (InternalsVisibleTo), 002 (`AgentVfxHandleMap`, for `describe_object`'s
handle input).

## Files to create
- `Assets/AgentVFX/InternalAccess/AgentVfxIntrospectionOps.cs`
  (`static partial class AgentVfxInternalBridge`, same convention as
  `AgentVfxNodeOps.cs`/`AgentVfxSlotOps.cs`):
  - `FindTypes(string query)` → enumerate `typeof(VFXGraph).Assembly.GetTypes()`
    (guide §4), filter by substring match on `FullName`/`Name` (case
    insensitive). Return fields: `fullName, name, namespace, baseType,
    isAbstract, isEnum, isGenericType`.
  - `DescribeType(string typeFullName)` → resolve via
    `Type.GetType`/assembly scan (share the resolver with `resolve_type`,
    task 005/003 — one type-resolution helper, referenced by both). Return,
    using `BindingFlags.Instance | Static | Public | NonPublic`:
    constructors, methods (name, return type, parameter names/types, generic
    arity, static/instance, public/non-public), properties, fields, nested
    types, base type, interfaces — all as strings/string-arrays, never a
    reflection object crossing the boundary.
  - `DescribeObject(string handleId)` → resolve via `AgentVfxIdMap` or
    `AgentVfxHandleMap` (same prefix dispatch as task 003). Return runtime
    type, fields, properties, current primitive values (via
    `AgentVfxExecValueCodec.Encode`, so complex members come back as further
    `$ref` handles), collection size if the object is an `ICollection`. Per
    guide §4: **do not** recursively serialize the whole object graph — one
    level deep only, nested complex values become new handles the agent can
    separately `describe_object` on if needed.
- New DTOs in `Assets/AgentVFX/Editor/AgentVfxDtos.cs`:
  `AgentVfxTypeSummaryDto[]` (find_types result), `AgentVfxTypeDetailDto2`
  or a genuinely new name (do not overload the existing
  `AgentVfxTypeDetailDto` — that one describes a *VFX node type* via
  `VFXLibrary`, a narrower, VFX-specific shape; this one describes an
  arbitrary CLR type via reflection. Pick a distinct name, e.g.
  `AgentVfxReflectedTypeDto`, so the two are never confused), and
  `AgentVfxObjectDescriptionDto`.
- Matching internal snapshot DTOs in
  `Assets/AgentVFX/InternalAccess/AgentVfxSnapshots.cs` (or a new
  `AgentVfxIntrospectionSnapshots.cs` if the existing file starts feeling
  overloaded — use judgment at implementation time, existing file is small
  enough today that adding to it is probably fine).
- `AgentVfxApi.cs`: thin pass-through methods
  (`FindInternalTypes`, `DescribeInternalType`, `DescribeInternalObject`),
  matching the existing method style exactly.

## Acceptance Criteria
- `vfx_internal_find_types` with query `"VFXContext"` returns at least
  `UnityEditor.VFX.VFXContext` itself plus concrete subclasses.
- `vfx_internal_describe_type` on `UnityEditor.VFX.VFXSlot` returns non-public
  members (verifies `BindingFlags.NonPublic` is actually applied — a test
  should assert a *specific* known non-public member appears, not just that
  the array is non-empty).
- `vfx_internal_describe_object` on a handle obtained from reading a real
  node (via task 005's `get_graph`/`enumerate`, or directly via
  `AgentVfxIdMap.GetOrCreateId` in a test) returns that object's actual
  runtime type name and at least one field/property value.

## Scope
Medium — three new read-only bridge methods, mostly reflection plumbing.

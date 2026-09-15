# 004 - Bounded Introspection Commands

## Goal

Let callers discover installed VFX types and members without guessing, while
using task 001 policy as the only discovery/invocation boundary.

## Dependencies

001-003.

## Files

- Create `Assets/AgentVFX/InternalAccess/AgentVfxIntrospectionOps.cs` as an
  `AgentVfxInternalBridge` partial.
- Add distinct reflection DTOs to `AgentVfxSnapshots.cs` and
  `AgentVfxDtos.cs`; do not reuse `AgentVfxTypeDetailDto`, which describes
  `VFXLibrary` catalogue entries.
- Add `FindInternalTypes`, `DescribeInternalType`, and
  `DescribeInternalObject` pass-throughs to `AgentVfxApi.cs`.

## Commands

- `FindTypes(string query)`: case-insensitive name/full-name match over policy-
  allowed types. Return full name, namespace, base type, flags, and assembly
  name.
- `DescribeType(string typeFullName)`: constructors, methods, properties,
  fields, nested types, base type, and interfaces. Include visibility, static
  flag, generic arity, parameter names/types/modifiers, return type, and a
  stable signature string used by overload selection.
- `DescribeObject(string assetPath, string handleId)`: open guarded graph,
  resolve handle in its scope, return runtime type and one level of readable
  field/property values. Skip indexers; catch getter exceptions per member and
  report their error instead of failing whole description.

Complex member values are encoded through `AgentVfxValueCodec`, producing
scoped handles where needed. Never recursively dump whole object graphs.

## Acceptance Criteria

- `VFXContext` search returns base and installed concrete subclasses.
- `VFXSlot` description contains a specific known non-public member from VFX
  Graph 17.4.0.
- Object description rejects a node id belonging to another guarded graph.
- Types outside policy are not discoverable or describable.

## Scope

Medium.

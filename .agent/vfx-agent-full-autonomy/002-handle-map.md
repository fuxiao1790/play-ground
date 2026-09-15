# 002 - Scoped Handle Registry

## Goal

Provide domain-lifetime identities for non-`VFXModel` reference objects while
keeping every graph model interoperable with existing convenience-command ids.

## Dependencies

001.

## Files

- Create `Assets/AgentVFX/InternalAccess/AgentVfxHandleMap.cs`.
- Update `AgentVfxIdMap.cs` only to recognize a `data:N` prefix for `VFXData`;
  existing `node:N` and `slot:N` identities remain unchanged.

## Design

- `AgentVfxHandleMap` uses `ConditionalWeakTable<object, IdBox>` plus
  `Dictionary<string, WeakReference<object>>`, matching `AgentVfxIdMap` lifetime
  behavior.
- Id format: `obj:N`. Values must be reference types. Unsupported boxed value
  types are encoded structurally or rejected by task 003, never stored as a
  misleading mutable handle.
- Each entry records its owning request graph when graph-derived. Resolving it
  for another graph throws.
- `VFXModel` never enters this map. Codec routing order is `VFXModel`,
  `UnityEngine.Object` asset, then general reference object.
- `VFXData` is a `VFXModel` in installed VFX Graph 17.4.0. It remains in
  `AgentVfxIdMap`; `data:N` prevents callers mistaking it for a visible node.
- Prefix resolver accepts `node:`, `slot:`, and `data:` through
  `AgentVfxIdMap`; `obj:` through `AgentVfxHandleMap`; unknown prefixes fail.
- Main-thread-only use means no locking is required; document this invariant.

## Acceptance Criteria

- Same reference and graph scope returns same id; distinct references return
  distinct ids.
- Stale, unknown-prefix, and cross-graph resolutions fail clearly.
- VFX nodes/slots keep ids returned by existing APIs; shared `VFXData` returns
  one `data:N` id across contexts.
- Tests exercise behavior through public introspection/executor APIs, not direct
  access from `PlayGround.Tests.EditMode` to internal classes.

## Scope

Small-medium.

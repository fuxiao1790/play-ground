# 001 - Executor Contract and Reflection Policy

## Goal

Preserve current assembly boundary and define one enforceable safety policy
before reflection code exists.

## Dependencies

None.

## Corrected Design

Do **not** add `InternalsVisibleTo`. Files in `InternalAccess/` already compile
into `Unity.VisualEffectGraph.Editor` through the `.asmref`; `AgentVFX.Editor`
should continue seeing only public bridge methods with primitive/JSON
signatures.

## Files

- Create `Assets/AgentVFX/InternalAccess/AgentVfxReflectionPolicy.cs`.
- Create `Assets/AgentVFX/InternalAccess/AgentVfxExecContract.cs` only if named
  constants/small internal request records make dispatch clearer. Do not expose
  VFX types in public signatures.

## Required Policy

- One `Exec(string requestJson)` request contains `asset`, `saveOnSuccess`, and
  `operations`; `asset` is required and opened with `OpenGraph`.
- Limit a request to 256 operations and any `enumerate` result to 10,000 items.
- Type lookup may search `typeof(VFXGraph).Assembly` and an explicit set of
  codec-supported UnityEngine/System value types. It must not scan arbitrary
  AppDomain assemblies as an invocation surface.
- Deny reflection on `Type`, `Assembly`, `MemberInfo`, delegates, pointers,
  open generics, filesystem/process/environment APIs, and asset-mutation APIs.
- Deny `call_static` by default. Future static methods require exact
  type/method/signature allowlist entries plus tests.
- For a `VFXModel` target, allow only request graph itself or a model whose
  `GetGraph()` is the same graph. Reject stale and cross-graph ids before member
  access.
- Allow non-model targets only when they are codec-supported immutable/value
  objects or scoped transient handles created by this request/graph.
- Walk base types deliberately for inherited non-public members. Reject
  ambiguous members and overloads with actionable messages.

## Acceptance Criteria

- No `InternalsVisibleTo` source or asmdef change is introduced.
- Policy has one type resolver and one target-scope validator reused by
  introspection and executor operations.
- Rejected type, static call, stale handle, cross-graph handle, operation-count
  overflow, and enumeration overflow each have distinct errors.
- Public API-level tests are planned in task 009; no internal implementation
  types are exposed merely to test them.

## Scope

Medium. Foundation for every later task.

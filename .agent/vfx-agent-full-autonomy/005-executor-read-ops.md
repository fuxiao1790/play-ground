# 005 - Executor Core and Read Operations

## Goal

Implement request parsing, validation, local bindings, reflection resolution,
and non-mutating operations before mutation behavior is added.

## Dependencies

001-004.

## Files

- Create `Assets/AgentVFX/InternalAccess/AgentVfxExecOps.cs` as an
  `AgentVfxInternalBridge` partial.
- Add `Exec(string requestJson) -> string` public bridge entry and an
  `AgentVfxApi.ExecInternal(string requestJson)` pass-through.

## Request and Result

- Parse exactly one object with required `asset`, optional
  `saveOnSuccess:false`, and required non-empty `operations` array.
- Open `asset` once through `OpenGraph` before resolving any handle.
- Preflight request shape, operation names, alias syntax/uniqueness, explicit
  type names, and statically resolvable member signatures before execution.
- Local bindings store both runtime value and declared type. `cast` changes the
  declared type used for later member lookup; a plain `object` dictionary is
  insufficient.
- Each result is encoded and returned in operation order. `as` stores same raw
  result for later `{"$local":"name"}` operands.

## Operations

- `get_graph`
- `resolve_type`
- `get` with optional `memberKind` (`field` or `property`)
- `enumerate` with 10,000-item bound
- `index` for arrays/`IList`
- `cast` with assignability check

`call` is task 006 because any method call may mutate. `call_static` is not in
the accepted schema; task 001 policy default-denies it.

Member resolution walks base types, includes public/non-public instance
members, rejects indexers in `get`, and rejects ambiguous field/property names
unless `memberKind` disambiguates.

## Failure Shape

Return `success:false`, `failureStage`, nullable zero-based `failedOperation`,
completed encoded results, exception type/message/stack trace, `saved:false`,
and `mayHaveMutated:false`. Do not leak a raw exception through CLI transport.

## Acceptance Criteria

- `get_graph -> get children -> enumerate` returns same VFX model ids as
  `AgentVfxApi.ReadGraph`.
- Local aliases work in target and collection positions.
- `cast` changes declared member lookup without changing identity.
- Invalid member and enumeration overflow return structured failure.
- Read-only executor never saves or dirties graph even if
  `saveOnSuccess:true` was requested.

## Scope

Large.

# 007 - CLI Wiring and Top-Level Node Contract

## Goal

Expose bounded reflection through Pipeline without double-encoding request JSON,
and fix the existing top-level-node CLI gap directly.

## Dependencies

004-006.

## Files

- Update `AgentVfxApi.cs` with thin JSON/DTO pass-throughs.
- Update `AgentVfxCommands.cs` with four main-thread commands:
  `vfx_internal_find_types`, `vfx_internal_describe_type`,
  `vfx_internal_describe_object`, and `vfx_internal_exec`.
- Correct stale `AgentVfxCommands.cs` comment claiming `com.unity.pipeline` is
  not installed.

## Binding Contract

`vfx_internal_exec` command accepts a Newtonsoft `JToken request`, then sends
`request.ToString(Formatting.None)` to `AgentVfxApi.ExecInternal`. Pipeline's
installed `CommandLineBinder` explicitly preserves JToken/JObject/JArray shapes;
do not force callers to put JSON inside a JSON string. Internal bridge boundary
still receives only a string.

`vfx_internal_describe_object` accepts both `assetPath` and `handleId`, ensuring
object lookup is graph-scoped.

## Top-Level Node Fix

- Define non-empty CLI sentinel `graph` for `vfx_node_create.parentId`.
- Update `AgentVfxInternalBridge.CreateNode` to treat null, empty, or `graph` as
  graph parent, preserving direct API compatibility while bypassing Pipeline's
  empty-string validation problem.
- Document and test sentinel. Do not make executor the only way to create a
  top-level Operator/Context.

## Acceptance Criteria

- Command registry lists all four new commands.
- Structured request reaches executor without double serialization.
- Calling `vfx_node_create` with `parentId:"graph"` creates top-level node.
- Existing null-parent `AgentVfxApi.CreateNode` behavior remains valid.
- Existing command signatures change only where sentinel documentation or new
  commands require it.

## Scope

Small-medium.

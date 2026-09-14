# 006 — Executor Mutation Ops + Transactional Wrapper

## Goal
`set`, `create`, `create_scriptable_object` operations, plus the
transaction semantics guide §14 requires: one `vfx_internal_exec` call is one
logical edit (Undo group; execute sequentially; on success `mark_dirty`
+ optional `save`; on failure, nothing partially-applied is worse than
necessary — see Design Validation below for exactly what "rollback" means
here).

## Dependencies
005 (extends the same dispatcher/`Exec` method).

## Files to change
- `Assets/AgentVFX/InternalAccess/AgentVfxExecOps.cs` (from task 005):
  - Add ops:
    - `set` — `{"target": "$x", "member": "name", "memberKind": ..., "value": <encoded>}`
      → reflect field/property, decode `value` against the member's real
      type, write it.
    - `create` — `{"type": "Full.Name", "constructorParameterTypes": [...], "args": [...]}`
      → resolve exact constructor overload (same overload-resolution
      approach as `call`), decode args, `Activator.CreateInstance` /
      `ConstructorInfo.Invoke`.
    - `create_scriptable_object` — `{"type": "Full.Name"}` →
      `ScriptableObject.CreateInstance(type)`, per guide §9. Note: any
      `VFXModel`-derived object created this way and later attached to the
      graph (via a subsequent `call`/`set` op, e.g. `graph.AddChild`) must
      get an `AgentVfxIdMap` id the moment it's first encoded as a handle
      (already guaranteed by task 003's `Encode` VFXModel-first check — no
      new logic needed here, just confirm it in the acceptance test).
    - `mark_dirty` — `{"target": "$x"}` (or implicit: mark the graph opened
      by this call's `get_graph`) → whatever Unity's own dirty-marking call
      is for a `VFXModel`/`VFXGraph` (check `AgentVfxCompileOps.cs`'s
      `Compile` for the existing precedent —
      [AgentVfxCompileOps.cs:14-20](../../Assets/AgentVFX/InternalAccess/AgentVfxCompileOps.cs#L14-L20)
      already does `SetExpressionValueDirty`/`CompileForImport`-adjacent
      calls; reuse rather than reinvent).
    - `save` — `{"asset": "..."}` → call the *existing*
      `AgentVfxInternalBridge.SaveGraph(assetPath)`
      ([AgentVfxInternalBridge.cs:35-41](../../Assets/AgentVFX/InternalAccess/AgentVfxInternalBridge.cs#L35-L41))
      rather than reimplementing serialize+`AssetDatabase.SaveAssets`.
  - Wrap the whole per-request operation loop (from task 005) with:
    ```
    Undo.SetCurrentGroupName("AgentVFX exec: " + assetPath);
    var group = Undo.GetCurrentGroup();
    try {
        // run operations, same failure handling as task 005
        // on success: nothing extra required beyond what ops already did
    } finally {
        Undo.CollapseUndoOperations(group);
    }
    ```
    Per index.md invariant 8, this Undo grouping is intentionally scoped to
    `vfx_internal_exec` only — do not add it to any existing convenience
    command in this task (that's Open Question 1 in index.md, explicitly
    out of scope).
  - "If one fails: return failed operation index" (guide §14) — on failure,
    operations already executed before the failing one are **not** rolled
    back automatically (Unity's `UnityEditor.VFX` internals have no
    generic transactional rollback; only the Undo stack could undo them,
    and auto-invoking `Undo.PerformUndo` from inside a command is out of
    scope/unverified). Document this explicitly in the response and in
    `.agent/vfx-graph-agent.md` (task 010): a failed `vfx_internal_exec`
    call may have partially mutated the graph up to `failedOperation`; the
    agent should re-run `vfx_graph_read`/`vfx_errors` to see the actual
    resulting state rather than assuming nothing happened. This is a
    deliberate scope decision (real rollback would need a full
    snapshot/restore of arbitrary VFX internal state, which nothing in this
    bridge currently does for any command) — not a bug to silently work
    around.

## Acceptance Criteria
- A `create_scriptable_object` → `set` (settings) → `call` (`graph.AddChild`)
  sequence produces a node that `vfx_graph_read` sees afterward, with the
  same id the exec call's own handle used.
- A failing 3rd operation in a 5-op sequence returns
  `{"success": false, "failedOperation": 2, ...}` and operations 0-1's
  effects are visible in a subsequent `vfx_graph_read` (proving/documenting
  the no-auto-rollback behavior above, not silently passing either way).
- `save` after a successful exec sequence actually writes the `.vfx` file
  (reuse `AgentVfxCompatibilityTests.CanSave`'s file-existence check
  pattern).

## Scope
Medium-large. Builds directly on 005's dispatcher; the new ops themselves are
individually small, the transactional wrapper and its documented limitation
are the part needing care.

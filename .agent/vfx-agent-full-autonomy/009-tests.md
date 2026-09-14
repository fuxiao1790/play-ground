# 009 — Tests

## Goal
Guide §24's test list, adapted to this codebase's actual test file
(`AgentVfxCompatibilityTests.cs`) and conventions. Per
`Docs/project-overview.md`/`.agent/vfx-graph-agent.md` §*Test Policy*: write
these, do not run them — hand exact class/method names to the user for the
Unity EditMode runner and review the exported XML before claiming anything
passed.

## Dependencies
001-008 (exercises everything built).

## Files to change
- `Assets/Tests/EditMode/AgentVfxCompatibilityTests.cs` — add tests
  following the existing `[SetUp]`/`[TearDown]`/`TestDir` fixture pattern
  already in this file. Guide §24's list, mapped to what actually exists
  here:
  - `AgentAssemblyCanAccessVfxInternals` — the task 001 smoke check, kept as
    a permanent regression test (not the throwaway scratch method from
    001's acceptance criteria).
  - `CanFindInternalTypes`, `CanDescribeNonPublicMembers` (task 004).
  - `CanExecGet`, `CanExecSet`, `CanExecInstanceMethod`,
    `CanExecStaticMethod`, `CanExecNonPublicMethod`,
    `CanExecCreateInstance`, `CanExecCreateScriptableObject`,
    `CanExecEnumerate`, `CanExecReferencePreviousResult` (tasks 005-006 —
    the last one specifically exercises the `"as"`/`$myName` local-alias
    chaining from task 005).
  - `CanReadFlowConnections`, `CanReadBlockOwnership` (childIndex),
    `CanReadNestedSlots`, `CanReadModelSettings` (task 008) — reuse the
    `MagicBoltTrail.vfx` fixture like `CanReadRealProductionGraph` does,
    since a bridge-created throwaway graph won't have real flow wiring to
    assert against.
  - `CanPerformFlowEditViaGenericExecutor` — the guide's own suggested
    acceptance test (§24): wire two contexts' flow slots via `call`/`set`
    exec ops (something `vfx_node_create`/no convenience command can do
    today, per `.agent/vfx-graph-agent.md`'s Not-Available list) and verify
    via the extended `vfx_graph_read`'s `flowConnections`.
  - `CanPerformPreviouslyUnsupportedEditViaGenericExecutor` — pick ONE more
    item from the Not-Available list (e.g. reading/writing an
    `AnimationCurve` slot value, or creating a genuine new top-level
    Operator, which is blocked today by the server-side `parentId`
    validation bug documented in `.agent/vfx-graph-agent.md`, but not by
    anything in this bridge itself — the exec path calls
    `graph.AddChild(model)` directly, bypassing the buggy CLI argument
    path entirely) and prove it end-to-end through the executor.
  - Handle-map unit-shaped tests for `AgentVfxHandleMap` (task 002's
    acceptance criteria, written as real `[Test]` methods here rather than
    left informal).
  - Value-codec round-trip tests for `AgentVfxExecValueCodec` (task 003's
    acceptance criteria).
- If `AgentVfxCompatibilityTests.cs` grows unwieldy, splitting into
  `AgentVfxExecCompatibilityTests.cs` alongside it is reasonable — use
  judgment at implementation time; not a hard requirement either way.

## Acceptance Criteria
- Every guide §24 test name (or this repo's adapted equivalent, listed
  above) exists as a real `[Test]` method.
- Test file compiles.
- A clear, numbered list of exact test class + method names (EditMode) is
  produced for the user to run, per this repo's test policy — do not run
  them, do not claim a result without the user's exported XML.

## Scope
Medium-large in line count, low in design risk — mechanical given tasks
001-008 are done first.

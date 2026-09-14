# 007 — CLI Command Wiring

## Goal
Expose every new bridge capability (tasks 002-006) through
`AgentVFX.Commands` as real `[CliCommand]`s, following the guide's final
architecture (§25): convenience commands unchanged, four new commands added
alongside them.

## Dependencies
004, 005, 006 (needs the bridge methods and `AgentVfxApi` pass-throughs to
already exist).

## Files to change
- `Assets/AgentVFX/Editor/AgentVfxApi.cs` — add pass-through methods for
  everything task 004/005/006 added to `AgentVfxInternalBridge`, in the same
  one-line style as every existing method here.
- `Assets/AgentVFX/Commands/AgentVfxCommands.cs` — add:
  ```csharp
  [CliCommand("vfx_internal_find_types", "...", MainThreadRequired = true)]
  [CliCommand("vfx_internal_describe_type", "...", MainThreadRequired = true)]
  [CliCommand("vfx_internal_describe_object", "...", MainThreadRequired = true)]
  [CliCommand("vfx_internal_exec", "...", MainThreadRequired = true)]
  ```
  matching the existing naming/description/`MainThreadRequired = true`
  convention exactly. `vfx_internal_exec`'s parameter is
  `string operationsJson` (bare JSON text, per index.md invariant 6) — do
  not attempt to give it a typed C# parameter shape; that was the explicit
  reason task 005 kept the executor's wire format as a string.

## Acceptance Criteria
- `unity command` command listing includes all four new commands (per
  `.agent/vfx-graph-agent.md`'s *Before Doing Anything* connectivity check
  pattern — this task doesn't need to run that check itself, but the
  resulting commands must be listable the same way `vfx_ping` etc. already
  are).
- Existing commands' behavior/signatures are untouched (diff review: this
  task should show only additions to `AgentVfxCommands.cs`/`AgentVfxApi.cs`,
  no changes to existing lines).

## Scope
Small — pure wiring, no new logic.

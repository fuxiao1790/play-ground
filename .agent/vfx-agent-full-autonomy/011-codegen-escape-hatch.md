# 011 — Codegen Escape Hatch (Optional)

## Goal
Guide §19-20's `vfx_codegen_run` — a last-resort path for operations the
generic executor genuinely cannot express (complex generic calls,
delegates/callbacks, `ref`/`out` struct parameters, multi-step factory
logic). **Only build this if task 009's acceptance tests reveal a real gap
the executor doesn't close** — see index.md Open Question 2. Do not build
speculatively.

## Dependencies
001-010 complete and evaluated first.

## Trigger Condition
Before starting this task, re-check `.agent/vfx-graph-agent.md`'s (updated,
per task 010) Known Capabilities and Limitations section. If every
previously-documented gap is now closed via `vfx_internal_exec`, stop here
and report that this task is not needed rather than implementing it anyway.

## Files to create (if triggered)
- `Assets/AgentVFX/InternalAccess/Generated/` — new directory under the
  existing `.asmref` area (guide §20), so anything here compiles into
  `Unity.VisualEffectGraph.Editor` with the same internal access as the rest
  of `InternalAccess/`.
- `Assets/AgentVFX/InternalAccess/IAgentVfxGeneratedTask.cs`:
  ```csharp
  namespace AgentVFX.Internal
  {
      public interface IAgentVfxGeneratedTask
      {
          string Execute(string json);
      }
  }
  ```
- A bridge method (`AgentVfxCodegenOps.cs`, same partial-class convention)
  that: writes a caller-supplied `.cs` file under `Generated/`, triggers a
  Unity recompile, waits for completion, locates and instantiates the
  generated `IAgentVfxGeneratedTask` implementation via reflection, invokes
  `Execute`, returns the result, then deletes the generated file. This
  costs a full compile/domain-reload per call (guide §20) — document that
  cost prominently in both the command description and
  `.agent/vfx-graph-agent.md`.
- CLI command `vfx_codegen_run` in `AgentVfxCommands.cs`, same wiring
  pattern as task 007.

## Acceptance Criteria (if triggered)
- A generated task can access an internal `UnityEditor.VFX` type that
  proved unreachable through `vfx_internal_exec` in task 009 (the specific
  scenario found there).
- The generated `.cs` file is cleaned up after execution (verify no leftover
  file in `Generated/` after a test run).
- `.agent/vfx-graph-agent.md` documents this as "not the normal editing
  path" (guide §19) with a clear description of when to reach for it vs.
  `vfx_internal_exec`.

## Scope
Medium-large if triggered; zero if not. Evaluate before starting.

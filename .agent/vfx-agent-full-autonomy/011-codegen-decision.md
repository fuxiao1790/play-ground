# 011 - Runtime Codegen Rejected

## Decision

Do not implement `vfx_codegen_run` as part of this plan.

## Dependencies

Review task 009 XML only if deciding whether a separate follow-up plan is
needed. No implementation task depends on this record.

## Rationale

- Writing caller-supplied C# under an `.asmref` turns graph editing into
  arbitrary Editor code execution and bypasses path/type/member policy.
- Triggering compilation causes a domain reload, invalidating bridge ids and
  interrupting the command that would need to clean up and resume.
- Cleanup after compile failure/domain reload cannot be guaranteed by a simple
  synchronous command.
- Installed Pipeline already has a general eval facility; adding a second code
  execution mechanism would duplicate risk without solving VFX internal access
  safely.
- Installed VFX Graph exposes flow linking directly, and unified codec covers
  curves/assets. No currently documented gap requires code generation.

## If a Gap Remains

After task 009 XML is reviewed, record exact blocked type/member/signature and
write a new narrow plan. Preferred order:

1. express it through existing typed bridge mechanisms;
2. add one audited convenience command;
3. add one exact static-signature allowlist entry to reflection policy;
4. consider offline generated bridge source only with explicit user approval,
   recovery protocol, and separate security review.

Runtime caller-supplied codegen is not an automatic fallback.

## Acceptance Criteria

- No `Generated/`, `IAgentVfxGeneratedTask`, or `vfx_codegen_run` code is added.
- Final docs do not advertise runtime code generation.
- Any remaining capability gap receives a separately reviewed plan tied to a
  concrete failing test or real requested operation.

## Scope

Decision record only; zero implementation.

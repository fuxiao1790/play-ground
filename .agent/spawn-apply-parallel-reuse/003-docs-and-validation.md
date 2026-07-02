# Docs And Validation

## Change

Update design docs and run focused validation.

## Acceptance Criteria

- Spawn flow docs describe command lists and single-threaded Burst reuse.
- Profiling docs no longer describe worker-lane imbalance as expected.
- Build or focused Unity tests run, or failure is captured with exact cause.

## Validation Result

- `dotnet build .\PlayGround.Runtime.csproj`: passed.
- `dotnet build .\PlayGround.Tests.PlayMode.csproj`: passed.
- `dotnet build .\PlayGround.Tests.EditMode.csproj`: passed.
- Focused Unity PlayMode run for `ProjectileSpawnPipelineTests`: Unity exited
  before writing result XML while existing `Unity.exe` processes were already
  running for this project. The sparse log ended at project-path setup with
  return code `1`, so no test result was produced.

## Dependencies

Depends on `001-single-thread-expansion.md` and
`002-single-thread-reuse.md`.

## Scope

Small.

# Implementation Log

## Status

Complete; Unity package-cache compilation remains externally blocked.

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-swept-types.md | Complete | Declarations added. Static validation passed; compile check remains baseline-blocked by unrelated `TargetProxyUpdateEvent` source error in `CombatTargetProxy.cs`. |
| 002-swept-box-geometry.md | **Reopened 2026-08-02** | Box must be the travel corridor only: `boxHalfExtents.x = dist * 0.5` with no along-travel extension, `BuildSweptBox` → `TryBuildTravelCorridor` returning `false` on a degenerate step, one `SupportExtent` call instead of two. See index Revision Log. |
| 003-authoring-flag-and-exclusivity.md | Complete | Authoring/compiler/request/template chain updated. Static validation passed; compile check baseline-blocked. |
| 004-expansion-command-fanout.md | Complete | Added swept command list and fan-out; static cleanup/path validation passed. Compile check baseline-blocked. |
| 005-spawn-apply-lane-split.md | Complete | Added distinct swept apply pool/system; query/archetype static checks passed. Compile baseline-blocked. |
| 006-sweep-origin-capture.md | **Reopened 2026-08-02** | Superseded. Previously completed as `006-swept-movement-system.md` (swept mover + discrete `WithNone`). Task redesigned: delete `SweptProjectileMovementSystem`, revert `ProjectileMovementSystem`, add `SweptProjectileOriginSystem`. See index Revision Log. |
| 007-swept-collision-system.md | **Reopened 2026-08-02** | Continuous check must run *on top of* the discrete check, not instead of it: two `CombatCollisionMath.Hit` calls per candidate (discrete at `Position`, then corridor), and the broadphase query region becomes the union of the corridor AABB and `collision.BoundsMin/Max`. See index Revision Log. |
| 008-tests.md | Needs update | Complete against the old 006. Two items changed: the double-integration test loses its `WithNone` rationale, and a new capture-ordering test was added (`Origin` must equal the pre-integration position). |
| 009-docs-update.md | Unblocked 2026-08-02 | Blocking conflict was stale task text, now corrected: `CombatSweepMath` is described as swept-box geometry, and the collision entry as nearest-first ordering. No TOI language remains. |

## Completed Tasks

- 001-swept-types.md (compile validation baseline-blocked; user directed continuation).
- 002-swept-box-geometry.md (compile validation baseline-blocked; user directed continuation).
- 003-authoring-flag-and-exclusivity.md (compile validation baseline-blocked; user directed continuation).
- 004-expansion-command-fanout.md (compile validation baseline-blocked; user directed continuation).
- 005-spawn-apply-lane-split.md (compile validation baseline-blocked; user directed continuation).
- ~~006-swept-movement-system.md~~ — superseded 2026-08-02, see 006-sweep-origin-capture.md.
- 007-swept-collision-system.md (compile validation baseline-blocked; user directed continuation).
- 008-tests.md (test build baseline-blocked in Unity package cache before test assemblies).

## Blockers

- Baseline compile-check failure persists: `Assets/Scripts/System/Targets/CombatTargetProxy.cs(381)` cannot resolve `TargetProxyUpdateEvent` in `PlayGround.Sim.CompileCheck.csproj`. User directed continuation. **Unrelated to this feature** — it predates the branch and blocks every compile validation below.
- ~~Task 009 wording conflicts with the architecture.~~ Resolved 2026-08-02: the task text was stale, not the implementation. Corrected in `009-docs-update.md`; 009 is ready to run.

## Open Work After The 2026-08-02 Revisions

1. **002** — corridor-only box: `boxHalfExtents.x = dist * 0.5`, rename to `TryBuildTravelCorridor` with a `bool` return for degenerate steps, drop the along-travel `SupportExtent` call and the shape fallback.
2. **006** — delete `SweptProjectileMovementSystem.cs`, remove `WithNone<SweptProjectileTag>` from `ProjectileMovementJob`, add `SweptProjectileOriginSystem`. Do **not** touch `ProjectileSpawnApplySystem._deadSlotQuery`'s `WithNone` — different exclusion, still required.
3. **007** — add the discrete test alongside the corridor test (hit on either, short-circuit `||`), and widen the broadphase query to the union of corridor AABB and `collision.BoundsMin/Max`.
4. **008** — drop the `WithNone`-on-movement rationale; add the capture-ordering test, the corridor-length test, and the endpoint-only-hit test that guards the discrete branch.
5. **009** — unblocked; run after the above land.

Note 001, 003, 004, 005 are unaffected by both revisions.

## Validation Summary

## Revision Completion 2026-08-02

- 002 complete: corridor-only `TryBuildTravelCorridor`; zero-length paths skip corridor.
- 006 complete: deleted duplicate swept mover; shared movement follows origin capture.
- 007 complete: collision tests current footprint plus corridor, with union broadphase bounds.
- 008 complete: geometry, endpoint-only discrete, origin-capture, and no-double-move guards updated.
- 009 complete: docs updated to match corridor-only and shared-movement design.
- `git diff --check` passed. `dotnet build PlayGround.Sim.csproj --no-restore` reached only Unity package-cache `PassesData.cs` errors `CS8168` and `CS8347` before project source compilation.

- Task 001: `git diff --check` passed. `dotnet build PlayGround.Sim.CompileCheck.csproj` failed on the unrelated target-proxy error above; task sequence stopped.
- Task 002: `git diff --check` passed. Compile check not rerun because the known baseline error blocks it.
- Task 003: `git diff --check` and static path checks passed. Compile check not rerun because the known baseline error blocks it.
- Task 004: `git diff --check` and list lifecycle/routing checks passed. Compile check not rerun because the known baseline error blocks it.
- Task 005: `git diff --check` and lane archetype/query/static checks passed. `PlayGround.Sim.csproj` build hit known package-generated baseline errors before project compilation.
- Task 006: static query/order/body and diff checks passed **against the superseded design**; that validation no longer applies. Revalidate after the origin-capture rework.
- Task 007: `git diff --check` and static handle/broadphase/candidate checks passed. Compile check not run because the known baseline error blocks it.
- Task 008: static test/production guard checks passed. `PlayGround.Tests.EditMode.csproj --no-restore` stopped in baseline Unity package-cache `PassesData.cs` errors (`CS8168`, `CS8347`) before project test assemblies.
- Task 009: blocked before edits by documentation/design conflict above.

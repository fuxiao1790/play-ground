> **LANDED (Increment 1) — partly superseded.** Implemented in code. The lifetime unification stands, but it still uses per-domain active tags; Increment 2 replaces those with the generic `Active` ([tasks/011](011-generic-active.md)). Authoritative end-state: `../context/002` + `../context/005`.

# Task 001: Unify projectile & AoE lifetime into one generic system

## Execution Role
This task is intended for a lower-cost implementation agent. Do not make architecture decisions. Follow this file and the linked context files exactly.

## Goal
Replace `ProjectileLifetimeSystem` and `AoeLifetimeSystem` with one generic `CombatLifetimeSystem` driven by a new enableable `CombatLifetimeComponent`, preserving all current expiry/deactivation/VFX behavior. Move AoE pulse VFX into a small dedicated `AoePulseVfxSystem`.

## Required Reading
- `../context/002-target-architecture.md` (§1.5, §2 Lifetime)
- `../context/003-data-flow.md` (§6)
- `../context/004-system-ordering.md` (phase 9.1)
- `../context/005-decision-log.md` (D-LIFETIME-PULSE, D-LIFETIME-VFX, D2)

## Design Decisions Already Made
- Introduce enableable `CombatLifetimeComponent { float Remaining; }` in `System/Common/`. It replaces `ProjectileLifetimeComponent.RemainingLifetime` and `AoeLifetimeComponent.RemainingLifetime`.
- Keep the per-domain active tags (`ProjectileActiveTag`, `AoeActiveTag`) — do NOT introduce a generic `Active` (D2).
- Pulse AOEs (`Lifetime <= 0`) spawn with `CombatLifetimeComponent` **disabled**; the unified system skips them; `AoeCollisionSystem` still deactivates them the same tick. Remove `AoeLifetimeComponent.IsPulse`.
- Unified despawn VFX (`Trigger=2`) uses `area = max(render.VisualScale.x, render.VisualScale.y)` for both domains.
- Pulse VFX (`Trigger=3`) moves to `AoePulseVfxSystem`, keyed on `AoePulseVfxComponent` (unchanged component).

## Why This Task Exists
Design §4: the countdown+disable logic was never domain-specific; the split into two systems is incidental. The unification is a self-contained clarity win and a foundation other tasks rely on (it removes `ProjectileLifetimeComponent`/`AoeLifetimeComponent` from the archetypes the spawn refactor rebuilds).

## Current Code References
```
Assets/Scripts/System/Projectile/ProjectileLifetimeSystem.cs
- ProjectileLifetimeSystem (ISystem), ProjectileLifetimeJob
- Current behavior: over (ProjectileTag, ProjectileActiveTag): RemainingLifetime -= dt;
  on <=0 disable ProjectileActiveTag + CombatRenderActiveTag, enqueue despawn VFX (Trigger=2,
  area = max(render.VisualScale.xy)).
- Required outcome: deleted; behavior provided by CombatLifetimeSystem over the projectile query.

Assets/Scripts/System/Aoe/AoeLifetimeSystem.cs
- AoeLifetimeSystem (ISystem), AoeLifetimeJob, AoePulseVfxJob
- Current behavior: AoeLifetimeJob over (AoeTag, AoeActiveTag) skips IsPulse==1; counts down;
  on <=0 disable AoeActiveTag + CombatRenderActiveTag + despawn VFX (Trigger=2, area=area.Size).
  AoePulseVfxJob emits periodic pulse VFX (Trigger=3) for non-pulse lingering AOEs.
- Required outcome: deleted; countdown/disable provided by CombatLifetimeSystem; pulse VFX
  provided by new AoePulseVfxSystem.

Assets/Scripts/System/Projectile/ProjectileEcsComponents.cs
- ProjectileLifetimeComponent { float RemainingLifetime; }
- Required outcome: removed; replaced by CombatLifetimeComponent.

Assets/Scripts/System/Aoe/AoeEcsComponents.cs
- AoeLifetimeComponent { float RemainingLifetime; int IsPulse; }
- Required outcome: removed; lifetime via CombatLifetimeComponent; IsPulse via disabled-lifetime state.

Assets/Scripts/System/Projectile/ProjectileSpawnSystem.cs (archetypes + reset)  — uses ProjectileLifetimeComponent
Assets/Scripts/System/Aoe/AoeSpawnSystem.cs (archetype + LifetimeFor/RecordAoeReset/AoeSpawnJob) — uses AoeLifetimeComponent.IsPulse
Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs — reads/writes lifetime.RemainingLifetime
Assets/Scripts/System/Aoe/AoeCollisionSystem.cs — reads lifetime.IsPulse
Assets/Scripts/System/Common/CombatRoot.cs — none directly (lifetime set via request.Lifetime)
```

## Files To Modify
- `System/Common/CombatEcsComponents.cs` (or a new `System/Common/CombatLifetimeComponent.cs`) — ADD `CombatLifetimeComponent`. Additive.
- `System/Projectile/ProjectileEcsComponents.cs` — REMOVE `ProjectileLifetimeComponent`. Replacement.
- `System/Aoe/AoeEcsComponents.cs` — REMOVE `AoeLifetimeComponent`; remove `IsPulse`. Replacement. (Keep `AoePulseVfxComponent`.)
- `System/Projectile/ProjectileSpawnSystem.cs` — archetypes: replace `ProjectileLifetimeComponent` with `CombatLifetimeComponent`; in reset set `Remaining = request.Lifetime` and enable the lifetime component. (This file is rewritten by Task 004; here just keep it compiling.)
- `System/Aoe/AoeSpawnSystem.cs` — archetype: replace `AoeLifetimeComponent` with `CombatLifetimeComponent`; set `Remaining`, enable lifetime for `Lifetime>0`, **disable** for pulse (`Lifetime<=0`). Remove `IsPulse` usage. (Rewritten by Task 006; keep compiling here.)
- `System/Projectile/ProjectileCollisionSystem.cs` — replace `ref ProjectileLifetimeComponent lifetime` reads/writes with `CombatLifetimeComponent` (and its enabled state where it sets `RemainingLifetime = 0`).
- `System/Aoe/AoeCollisionSystem.cs` — replace `lifetime.IsPulse == 1` checks with "is `CombatLifetimeComponent` disabled" (pass `EnabledRefRO<CombatLifetimeComponent>` or query split). Keep the same deactivation behavior for pulse.
- Tests: `Assets/Tests/PlayMode/AoeSimulationTests.cs` — replace `AoeLifetimeSystem` with `CombatLifetimeSystem` (+ `AoePulseVfxSystem` if a test exercises pulse VFX); `SpawnCircle` sets pulse via `Lifetime<=0`.

## Files To Create
- `System/Common/CombatLifetimeSystem.cs` — `CombatLifetimeSystem : ISystem`, `SimulationSystemGroup`, ordered at phase 9.1 (before both collision systems). Two `IJobEntity`s (or one generic job scheduled twice) over `(ProjectileTag, ProjectileActiveTag, enabled CombatLifetimeComponent)` and `(AoeTag, AoeActiveTag, enabled CombatLifetimeComponent)`; each counts down, disables the domain active tag + `CombatRenderActiveTag`, emits despawn VFX. Namespace `PlayGround.System.Common`.
- `System/Aoe/AoePulseVfxSystem.cs` — `AoePulseVfxSystem : ISystem`, AoE-only; ports `AoePulseVfxJob` from `AoeLifetimeSystem`. Order after lifetime, before render prepare.

## Files To Delete
- `System/Projectile/ProjectileLifetimeSystem.cs`
- `System/Aoe/AoeLifetimeSystem.cs`

## Required Changes
1. Add `CombatLifetimeComponent : IComponentData, IEnableableComponent { public float Remaining; }` with an `ECS Lifecycle:` comment (see context 002 §1.5).
2. Create `CombatLifetimeSystem`. For the projectile job, mirror `ProjectileLifetimeJob` exactly but read/write `CombatLifetimeComponent.Remaining` and require it enabled (`[WithAll(typeof(ProjectileTag), typeof(ProjectileActiveTag))]` + the component as `ref` only matches enabled by default). For the AoE job, mirror the **non-pulse** branch of `AoeLifetimeJob` (countdown + disable + despawn VFX) — pulse AOEs are excluded because their `CombatLifetimeComponent` is disabled. Use `area = max(render.VisualScale.x, render.VisualScale.y)` in both despawn VFX writes.
3. Use the same VFX plumbing the old systems used: a `NativeQueue<VfxPendingSpawn>` + `VfxFlushJob` to the scope `VfxSpawnRequestElement` buffer (copy from `ProjectileLifetimeSystem`/`AoeLifetimeSystem`). Both domain jobs can share one queue (schedule the AoE job after the projectile job, as `AoeLifetimeSystem` already chains its two jobs because `NativeQueue` parallel writers can't overlap across jobs).
4. Create `AoePulseVfxSystem` by moving `AoePulseVfxJob` and its VFX queue/flush out of `AoeLifetimeSystem`. Keep `[WithAll(AoeTag, AoeActiveTag)]` and the `Interval<=0 || lifetime.IsPulse==1` guard becomes `Interval<=0 || CombatLifetimeComponent disabled` (pulse one-shots have lifetime disabled). NOTE: today pulse one-shots return early in the pulse-VFX job; preserve that (a disabled-lifetime AoE is a pulse one-shot → no periodic pulse VFX).
5. Update both spawn systems' archetypes and reset paths to use `CombatLifetimeComponent` and set its enabled state (projectile: always enabled; AoE: enabled iff `Lifetime>0`).
6. Update both collision systems to read the renamed lifetime data.
7. Register the two new systems and remove the two deleted systems from any explicit system lists (search for `ProjectileLifetimeSystem`/`AoeLifetimeSystem` references in tests and any bootstrap).
8. Recompile.

## Behavior Preservation Requirements
- Projectile expiry timing, deactivation (active + render tags), and despawn VFX unchanged.
- Lingering AoE expiry + despawn deactivation unchanged.
- Pulse AoE: hit once then deactivated the same tick (via collision) — unchanged.
- Pulse VFX cadence (`Trigger=3`) unchanged.

## Intentional Behavior Changes
- AoE **despawn** VFX area now uses `max(render.VisualScale.xy)` instead of `AoeAreaComponent.Size` (D-LIFETIME-VFX). Low-risk visual-only change. If validation finds it wrong, apply the documented fallback (Aoe apply copies `AreaSize` into `render.VisualScale`).

## Out of Scope
- Generic `Active` flag (D2). Keep `ProjectileActiveTag`/`AoeActiveTag`.
- Any spawn-pipeline restructuring (Tasks 002–006).

## Dependencies
None. This is the first task.

## Follow-Up Tasks
- Task 003 / 005 build new spawn-apply systems that must set `CombatLifetimeComponent` the same way this task establishes.

## Implementation Constraints
- ECS: `IEnableableComponent` toggling is NOT a structural change — use `SetComponentEnabled` / `EnabledRefRW`. Do not add/remove the component at runtime.
- Burst: keep jobs `[BurstCompile]`; `CombatLifetimeComponent` is blittable.
- Disposal: dispose the VFX `NativeQueue` each frame exactly as the old systems do; no new persistent handles.
- Naming/folders: `System/Common/` for the shared system/component; `System/Aoe/` for the pulse VFX system.
- Add/keep `ECS Lifecycle:` comments per `Docs/coding-standards.md`.

## Step-by-Step Implementation Plan
```
1. Add CombatLifetimeComponent (enableable) in System/Common.
2. Create CombatLifetimeSystem with projectile + aoe countdown jobs + VFX flush (ported).
3. Create AoePulseVfxSystem (ported AoePulseVfxJob + VFX flush).
4. Remove ProjectileLifetimeComponent / AoeLifetimeComponent; update archetypes + reset in
   ProjectileSpawnSystem and AoeSpawnSystem (set Remaining, set enabled state).
5. Update ProjectileCollisionSystem + AoeCollisionSystem to the renamed lifetime data / pulse-as-disabled.
6. Delete ProjectileLifetimeSystem.cs and AoeLifetimeSystem.cs (+ .meta).
7. Update AoeSimulationTests.cs system list + pulse spawn setup.
8. Recompile; run AoeSimulationTests.
```

## Acceptance Criteria
```
- [ ] CombatLifetimeComponent (enableable) exists in System/Common with an ECS Lifecycle comment.
- [ ] CombatLifetimeSystem counts down both domains and disables active+render tags on expiry.
- [ ] AoePulseVfxSystem emits pulse VFX; pulse one-shots emit none.
- [ ] ProjectileLifetimeComponent and AoeLifetimeComponent (and IsPulse) no longer exist.
- [ ] ProjectileLifetimeSystem.cs and AoeLifetimeSystem.cs are deleted.
- [ ] Pulse AoE disables CombatLifetimeComponent at spawn; unified system skips it.
- [ ] AoeSimulationTests pass (pulse-once, lingering-expire, reuse).
- [ ] Repo compiles.
```

## Validation
- Compile: Unity Editor recompiles `PlayGround.Runtime` with no errors.
- Tests: run `AoeSimulationTests` (Test Runner, PlayMode). `PulseHitsOverlappingTargetOnce`, `LingeringExpiresAndDeactivates`, `PulseEntityIsReusedOnRespawn` must pass after updating the system list.
- Manual: Play `Main.unity`; confirm projectiles expire and disappear, AOEs expire, pulse AOEs hit once. Watch despawn VFX still fires.

## Risk Level
Medium — touches archetypes, two collision systems, and spawn reset paths; the pulse-as-disabled mapping is the subtle part. Mitigated by being behavior-preserving and test-covered.

## Failure Modes
- **Pulse AoE never deactivates or never hits:** lifetime component not disabled at spawn, or unified query still matches it. Detect via `PulseHitsOverlappingTargetOnce`.
- **Lingering AoE never expires:** countdown not running (component disabled by mistake). Detect via `LingeringExpiresAndDeactivates`.
- **VFX leak / missing despawn VFX:** queue not disposed or flush job not scheduled. Detect via Unity persistent-allocation log + visual check.

## Rollback Strategy
Revert the task's commit: restore the two deleted systems and the two removed components, revert archetype/collision edits. The change set is isolated to lifetime; no other task depends on the *new* names until Task 003+.

## Notes for Future Tasks
- Tasks 003 and 005 will rebuild the spawn-apply archetypes; they must keep `CombatLifetimeComponent` and set its enabled state exactly as established here (projectile enabled; AoE enabled iff `Lifetime>0`).

# Implementation Log

## Status
Awaiting user validation

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-fix-regen-revive.md | Complete | Branchless depleted-health guard restored. Static source check and `git diff --check` passed; Unity XML pending user run. |
| 002-handshake-contract.md | Complete | Added deferred proxy spawn/despawn contracts and unmanaged create results. Static source inspection and `git diff --check` passed; Unity tests deferred to user. |
| 003-spawn-result-bridge.md | Complete | Added Presentation spawn-result bridge. Static source checks and `git diff --check` passed; Unity tests deferred to user. |
| 004-despawn-lane.md | Complete | Added death detection and Presentation despawn bridge. Static source checks and `git diff --check` passed. Unity tests deferred to user. |
| 005-gameobject-side.md | Complete | Pooled actors await proxy confirmation; ECS death now triggers deferred reclaim. Static source checks and `git diff --check` passed; Unity tests deferred to user. |
| 006-tests-and-docs.md | Complete | Added deferred lifecycle coverage and protocol docs. Frame-marker validation remains pending; phase-order TODOs preserved. |

## Completed Tasks
- 001-fix-regen-revive.md - `Assets/Scripts/System/Targets/ResourceRegenSystem.cs`
- 002-handshake-contract.md - `Assets/Scripts/System/Targets/ICombatTarget.cs`, `Assets/Scripts/System/Targets/TargetProxyEvents.cs`, `Assets/Scripts/System/Targets/CombatTargetProxy.cs`, `Assets/Scripts/System/Targets/TargetProxyCreateApplySystem.cs`, `Assets/Scripts/System/Core/CombatScopeOwner.cs`
- 003-spawn-result-bridge.md - `Assets/Scripts/System/Presentation/CombatActorSpawnBridge.cs`
- 004-despawn-lane.md - `Assets/Scripts/System/Targets/CombatDespawnOnDeathSystem.cs`, `Assets/Scripts/System/Presentation/CombatTargetBridge.cs`, `Assets/Scripts/System/Presentation/CombatDespawnBridge.cs`, `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`
- 005-gameobject-side.md - `Assets/Scripts/Spawn/MobPool.cs`, `Assets/Scripts/Spawn/SpawnController.cs`, `Assets/Scripts/Mob/MobRoot.cs`
- 006-tests-and-docs.md - PlayMode lifecycle coverage, dependent AOE fixture
  handshake update, deferred proxy lifecycle docs, spawn references, and ADR-007

## Blockers
- None

## Validation Summary
- 001: static source check and `git diff --check` passed. Unity tests deferred to user; XML required.
- 002: static source inspection and `git diff --check` passed. Unity tests deferred to user; XML required.
- 003: static source check and `git diff --check` passed. Unity tests deferred to user; XML required.
- 005: static source checks and `git diff --check` passed. Unity tests deferred to user; XML required.
- 006: static source/doc checks and `git diff --check` passed. Unity PlayMode
  XML and frame-marker trace remain user-owned. The phase-order TODOs must stay
  until the trace confirms current player-loop ordering.
- Compile-only `dotnet build PlayGround.Tests.PlayMode.csproj --no-restore` was
  blocked before project compilation by missing generated-project analyzer
  `C:\Users\user\.vscode\extensions\visualstudiotoolsforunity.vstuc-1.2.2\Analyzers\Microsoft.Unity.Analyzers.dll`.
- Post-implementation compile fix: `SpawnController` now calls public
  `CombatRoot.CreateTargetProxy`; `EntityManager` remains internal to Sim.
- Mob visual lifecycle fix: pooled mobs prepare alive state before activation;
  `OnDisable` and despawn force the renderer off. Spawn regression test now
  asserts renderer is disabled while pending and enabled after confirmation.
- Pool physics fix: `InitializeForSpawn` now copies the rented transform position
  into `Rigidbody2D` before enabling simulation, preventing old body pose from
  restoring on the next physics tick. Spawn regression test uses a non-zero
  position and asserts Rigidbody2D position matches it.
- Pool physics fix 2 (reused instances flashing at their previous position):
  the fix above ran on an **inactive** GameObject, where `Rigidbody2D` has no
  native body and both `body.position` and `body.simulated` writes are dropped.
  `ProjectSettings/Physics2DSettings.asset` has `m_AutoSyncTransforms: 0`, so the
  `MobPool.Rent` transform write is not authoritative either until a physics step
  runs - body and Transform disagreed across activation.
  `BeginLife` now activates before calling `InitializeForSpawn`, and
  `InitializeForSpawn` sets `body.simulated = true` before `body.position`.
  Safe to activate first because `OnDisable` leaves the sprite off and
  `InitializeForSpawn` re-enables it within the same `Update`.
  Note the asymmetry that hid this: `Awake` also calls `InitializeForSpawn`, so a
  pooled instance's **first** activation re-ran it with a live body and looked
  correct; only reuse was broken. If a flash is still observed on the very first
  spawn of an instance, this diagnosis is wrong and the cause is elsewhere.

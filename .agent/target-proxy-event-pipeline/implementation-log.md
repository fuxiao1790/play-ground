# Implementation Log

## Status

Complete

## Task Progress

| Task | Status | Notes |
|---|---|---|
| 001-events-and-scope-wiring.md | Complete | Added unmanaged event types and scope buffers; static acceptance check passed. |
| 002-combat-target-proxy-rewrite.md | Complete | Enqueue-only mutators and pending-create bookkeeping; build blocked by upstream package errors. |
| 003-apply-systems.md | Complete | Added multi-scope create/update/delete apply systems and prescribed group ordering. |
| 004-registry-and-combatroot-cleanup.md | Complete | Removed dead synchronous entity-key registry bookkeeping. |
| 005-skilldriver-lazy-caster.md | Complete | Skill spawns now resolve caster proxy from live owner reference. |
| 006-actor-root-call-sites.md | Complete | Roots bind lazy caster owners and use registered-proxy deletion guard. |
| 007-test-updates.md | Complete | Lifecycle tests use scope buffers and explicit apply/update timing; real-root timing audited. |
| 008-docs-update.md | Complete | Contract and flow docs describe event/apply timing; existing TODOs preserved. |

## Completed Tasks

- 001: `TargetProxyEvents.cs`; `CombatScopeOwner.cs` buffer wiring.
- 002: `CombatTargetProxy.cs`; all lifecycle writes queue scope events.
- 003: proxy create/update/delete event drain systems.
- 004: registry and CombatRoot dead-code cleanup.
- 005: SkillDriver lazy caster lookup.
- 006: PlayerRoot and MobRoot call-site/deletion guard update.
- 007: PlayMode target-proxy lifecycle timing updates.
- 008: target-proxy event pipeline documentation.

## Blockers

- None.

## Validation Summary

- 001 static inspection: all three event types implement `IBufferElementData` and contain only value-type fields; `CombatScopeOwner.Acquire` adds all three buffers. Unity compilation deferred until dependent tasks exist.
- 002 `git diff --check` and direct-mutation scan passed. `dotnet build PlayGround.Sim.csproj --no-restore` blocked before project source compilation by upstream Unity RenderGraph `CS8168`/`CS8347` package errors. Old `Create` Entity-return consumers are intentionally deferred to tasks 004 and 007.
- 003 `git diff --check` and static event-drain/ordering inspection passed. Compile-check project lacks generated `project.assets.json` and has not yet included new source files; Unity regeneration is required before a full compile.
- 004 `git diff --check` and repository reference scan passed; only unrelated `CombatTargetSet.TargetsById` remains. Runtime build reaches pre-existing Unity package `PassesData.cs` `CS8168`/`CS8347` errors before changed source errors.
- 005 static signature/use scan and `git diff --check` passed. Actor roots intentionally retain old `BindCaster(Entity)` calls until task 006.
- 006 all `BindCaster` call sites and normal/SoftDie deletion paths scanned; `git diff --check` passed. GameLogic build reaches unrelated PackageCache `PassesData.cs` `CS8168`/`CS8347` errors.
- 007 `git diff --check` passed. Full Unity batch tests could not run because project is already open; .NET build is blocked by existing RenderGraph `CS8168`/`CS8347` errors. AoePlayMode and ProjectileTracking tests were audited and need no changes.
- 008 `git diff --check` and targeted TODO-preservation search passed. Out-of-scope reference-doc wording was left unchanged.
- Follow-up compile fix: create apply now writes `TargetFaction { Value = createEvent.Faction }`; proxy archetype adds managed `TargetCompanion` with `AddComponentObject` after creation. `git diff --check` passed. Full build remains blocked by existing RenderGraph `CS8168`/`CS8347`; no-dependency build lacks generated Temp RenderPipeline DLLs.

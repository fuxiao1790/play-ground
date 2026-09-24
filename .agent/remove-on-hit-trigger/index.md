---
name: remove-on-hit-trigger
description: Remove the OnHitTrigger skill-trigger mechanism end to end (authoring type, compiler wiring, runtime fields, ECS on-hit spawn plumbing, docs, tests)
---

# Remove On-Hit Trigger

## Summary

`OnHitTrigger` ([OnHitTrigger.cs](../../Assets/Scripts/Skills/Trigger/OnHitTrigger.cs)) is a
`TriggerLink` that fires a child skill immediately on a projectile/AOE hit. It compiles through
`SkillSetCompiler.AttachOnHitTarget` into six `Impact*`/`OnHit*` fields spread across
`RuntimeProjectileDefinition`, `RuntimeAoeDefinition`, `RuntimeTargetedDefinition`, gets walked by
`SkillDriver`'s registration passes into a shared `OnHitSpawnRef` (`IntervalChildTemplates.cs`),
and is carried at runtime on `ProjectileHitComponent.OnHitSpawn` / `AoeHitSpawnComponent.OnHitSpawn`
/ `TargetedSpawnCommand.OnHitSpawn`, fired from the collision jobs
(`AoeCollisionCore.EnqueueOnHitSpawn`, `ProjectileHitEmission.EnqueueOnHitSpawn`) into the same
spawn-event queues (`ProjectileSpawnEvent`/`ImpactAoeSpawnEvent`/`LingeringAoeSpawnEvent`/
`TargetedSpawnEvent`) that `TimedSpawnSystem` (interval spawn) and `StatusProcessSystem` (stack
detonation) also feed.

This plan removes the mechanism completely: the authoring type, the compiler branch, all six
runtime fields, `OnHitSpawnRef` itself, every ECS field/branch that exists solely to carry or fire
it, the docs sections describing it, and the tests exercising it. The shared spawn-event queues,
expansion systems, and apply systems are untouched — they keep serving `TimedSpawnSystem` and
`StatusProcessSystem` exactly as before.

## Grounding

- [Docs/reference/game-logic/skill-system.md](../../Docs/reference/game-logic/skill-system.md)
  §OnHitTrigger (~line 605) and the `compile()` pseudocode (~line 800) describe the intended
  compiler contract.
- [Docs/project-overview.md](../../Docs/project-overview.md) / `.agent/coding-standards.md`
  conventions: match existing style, no speculative abstractions.
- Verified via `grep` for the asset GUID (`413cb78f04f1d7d4b8f66cf67d062bbc`): the only asset
  referencing `OnHitTrigger.asset` is `Assets/ScriptableObjects/UI/SkillBar/TriggerCatalog.asset`.
  **No `SkillLoadout` currently wires an `OnHitTrigger` into a chain.** Removal is behavior-neutral
  for every piece of live authored content.
- Verified via `grep` for `ProjectileEventWriter.Enqueue|ImpactAoeEventWriter.Enqueue|...`: the
  spawn-event queues have exactly three producers — on-hit collision emission (being removed),
  `TimedSpawnSystem.cs` (interval spawn), `StatusProcessSystem.cs` (stack detonation). The queue
  types, `*SpawnExpansionSystem`s, and `*SpawnApplySystem`s are shared infrastructure and are not
  touched by this plan.
- `RuntimeTargetedDefinition.OnHitAoeSpawnDefinition`/`OnHitProjectileSpawnDefinition` were
  forward-declared for a "task 010" that was never built (see comment at
  [RuntimeTargetedDefinition.cs:28](../../Assets/Scripts/Skills/Runtime/RuntimeTargetedDefinition.cs#L28)).
  `SkillSetCompiler.AttachOnHitTarget` never sets them (only Projectile/AOE sources are wired), so
  `SkillDriver.BuildOnHitSpawnRef(RuntimeTargetedDefinition)` always returns `default`. These are
  dead placeholders for a cancelled feature — removed along with everything else
  ([[dead-code-is-not-a-constraint]]).

## Constraints & Invariants

- **Determinism / no behavior change for live content**: no loadout asset currently uses
  `OnHitTrigger`, so every edit here must be a pure no-op for existing gameplay. Any test whose
  outcome would change is by definition a test of the removed mechanism itself.
- **Burst/ECS field removal is all-or-nothing per struct**: `ProjectileHitComponent`,
  `AoeHitSpawnComponent`, archetypes, and job structs must all be updated together, since Burst
  jobs require the struct shapes they declare to match the archetypes they query
  ([Docs/reference/simulation/ecs-notes.md](../../Docs/reference/simulation/ecs-notes.md)).
- **Producer/consumer separation preserved**: `TimedSpawnSystem` and `StatusProcessSystem` must
  keep writing to `ProjectileSpawnEvent`/`ImpactAoeSpawnEvent`/`LingeringAoeSpawnEvent`/
  `TargetedSpawnEvent` unchanged. Do not touch those event struct definitions, the expansion
  systems, or the apply systems' non-on-hit code paths.
- **Editor assets are user steps**: `OnHitTrigger.asset` and `TriggerCatalog.asset` are Unity
  YAML ScriptableObjects. Per established project convention, these are never hand-edited — the
  final task hands the user exact editor steps instead ([[editor-steps-are-user-steps]]).
- **Test execution is deferred to the user**: agents must not run the Unity test runner
  ([Docs/project-overview.md](../../Docs/project-overview.md)). This plan edits test source, but
  the user runs and reports results.

## Mechanisms Reused vs. Introduced

- **Reused**: the shared spawn-event queue / expansion / apply pipeline stays exactly as-is,
  now with exactly two producers (interval, stack) instead of three.
- **Introduced**: nothing. This is a pure subtraction.
- **One bundled simplification**: `AoeHitSpawnComponent` becomes empty once `OnHitSpawn` is its
  only field, so the whole component is deleted (not left as an empty struct) — same for the
  `hit` parameter on `SpawnTemplateRefEmit.EmitProjectile`/`EmitAoe`, which becomes unused once the
  on-hit `Enqueue` line is gone.
- **Explicitly deferred (not part of this plan)**: `ProjectileHitPayload` (a `CombatHitPayload` +
  `OnHitSpawnRef` wrapper) becomes a redundant single-field wrapper once `OnHitSpawn` is removed —
  AOE and Targeted already carry `CombatHitPayload` unwrapped, so collapsing `ProjectileHitPayload`
  away would match that sibling shape. This touches ~9 additional test files for a stylistic-only
  win and is out of scope for "remove on hit trigger" — noted as a follow-up, not done here
  (avoids scope growth per `implement-tasks.md`'s "Do not broaden scope").

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive approach** (leave `OnHitSpawnRef`/`AoeHitSpawnComponent`/collision-job writer
  params in place, only delete the authoring `OnHitTrigger.cs` + catalog entry): resulting data
  flow unchanged; introduces nothing new; but leaves `OnHitSpawnRef.Enabled` permanently `false`
  forever — dead weight on every projectile/AOE archetype, every collision job signature, and every
  registration walk, with no way to ever populate it again. Long-term cost: permanent unreachable
  code the next reader must reverse-engineer as "maybe still used somewhere."
- **Refactor approach (this plan)**: resulting data flow drops one of three producers into the
  shared spawn-event queues; removes `OnHitSpawnRef`, `AoeHitSpawnComponent`, six `Runtime*Definition`
  fields, and every branch/parameter that exists only to carry them; existing concepts changed:
  `ProjectileHitComponent`/`AoeSpawnCommand`/`TargetedSpawnCommand`/`ProjectileHitPayload` shrink by
  one field each; collision systems drop 4 unused singleton-lane acquisitions each. Long-term
  benefit: collision systems no longer need to know about spawn-event queues they don't use;
  matches `focus-on-structural-change.md`'s phase-boundary principle.
- **Decision**: refactor (full removal). Already confirmed with the user (see conversation): "Full
  removal, plan first."

## Task List

| # | File | Depends on |
|---|---|---|
| [001](001-skills-authoring-and-compiler.md) | Skills authoring & compile-time (`OnHitTrigger.cs`, `SkillSetCompiler.cs`, `SkillLoadoutCompiler.cs`, three `Runtime*Definition.cs`) | none |
| [002](002-skilldriver-registration-walks.md) | `SkillDriver.cs` registration walks + `BuildOnHitSpawnRef` | 001 |
| [003](003-spawning-core-types.md) | `IntervalChildTemplates.cs` (`OnHitSpawnRef`), `SpawnTemplateComponents.cs` (`SpawnTemplateRefEmit`, `SpawnTemplateValidation`) | 002 |
| [004](004-projectile-pipeline.md) | Projectile ECS: components, collision systems, spawn-apply system | 003 |
| [005](005-aoe-pipeline.md) | AOE ECS: components, collision core, collision systems, spawn-apply system | 003 |
| [006](006-targeted-and-combatroot.md) | `TargetedSpawnPipeline.cs`, `CombatRoot.cs` | 003, 004, 005 |
| [007](007-tests.md) | Update/remove tests broken by 001–006 | 001–006 |
| [008](008-docs.md) | Update docs describing the removed mechanism | 001–006 |
| [009](009-editor-steps-handoff.md) | Hand user the two required Unity-editor steps | none (do last, informational) |

## Open Questions / Follow-ups

- `ProjectileHitPayload` wrapper collapse (see "Explicitly deferred" above) — not part of this
  plan; flag to the user as an optional follow-up once this lands.
- None of the task list requires a mid-flight decision from the user; all six field removals and
  every collision-system signature change are mechanical consequences of one root cause
  (`OnHitSpawnRef` going away). Stop and report if a task's execution surfaces a usage this
  research missed.

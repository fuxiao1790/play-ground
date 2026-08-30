---
name: remove-lingering-hit-gate
description: Remove AoeHitGateComponent so lingering AOEs collide every simulation tick instead of on a repeat-hit cooldown; rename the surviving tick-interval field to reflect its VFX-only purpose.
---

# Remove the Lingering AOE Hit Gate

## Summary

`AoeHitGateComponent` currently throttles `LingeringAoeCollisionSystem` so a
lingering AOE (e.g. a poison cloud) only calls `AoeCollisionCore.RunCollision`
once every `RepeatHitCooldownSeconds`, instead of every simulation tick — see
[LingeringAoeCollisionSystem.cs:197-208](../../Assets/Scripts/System/Aoes/LingeringAoeCollisionSystem.cs#L197-L208)
and the documented rationale at [aoe-system.md:226-229](../../Docs/reference/simulation/aoe-system.md#L226-L229)
("There is no global AOE tick. Repeat timing belongs to each lingering AOE.").

**Decision (confirmed by user):** remove this throttling entirely. Nothing
replaces it — a lingering AOE collides and can emit hits every tick it is
active, for as long as it remains alive. This is a deliberate gameplay
change, not a bug fix.

**Key finding that reshapes scope:** `AoeSpawnCommand.RepeatHitCooldownSeconds`
(the value `AoeHitGateComponent` is seeded from) is *not* exclusive to the
gate. The same field also drives `AoePulseVfxComponent.Interval` and
`VfxTimingData.TickInterval` — the pulse-VFX flash cadence — independently, at
[AoeSpawnApplySystem.cs:654-665](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L654-L665).
So this plan does **not** remove the tick-interval concept or its authoring
field; it removes only the *collision-gating* consumer of that value.

**Decision (confirmed by user):** the surviving field was misleadingly named
`RepeatHitCooldownSeconds` specifically because it was written for the gate.
Now that only VFX cadence uses it, rename `AoeSpawnCommand.RepeatHitCooldownSeconds`
to `TickIntervalSeconds` — which also matches the naming already used one
layer up the pipeline (`RuntimeAoeDefinition.TickIntervalSeconds`,
`AoeSpawnRequest.TickIntervalSeconds`, and the Inspector field
`LingeringAoeDefinition.tickIntervalSeconds`, none of which need to change).
This is a plain C# field rename on a runtime, code-constructed struct — not a
Unity-serialized asset field — so it carries no editor-authoring or migration
risk (confirmed: `AoeSpawnCommand` is built in code by `SkillDriver`/
`CombatRoot`, never itself a `[Serializable]` ScriptableObject field).

## Constraints & Invariants

| Constraint | Source | Plan response |
|---|---|---|
| `AoeHitGateComponent` is added to **both** Impact and Lingering AOE archetypes, but only `LingeringAoeCollisionSystem` reads/writes it — `ImpactAoeCollisionJob` requires it in its query yet never touches it. | [AoeSpawnApplySystem.cs:44-61,303-325](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L44-L61) | Remove it from both archetypes and both queries, not just the Lingering side — leaving it on Impact after this change would be silent dead weight with no consumer at all. |
| `AoeSpawnCommand.RepeatHitCooldownSeconds` also feeds `AoePulseVfxComponent`/`VfxTimingData` (VFX), independent of the gate. | [AoeSpawnApplySystem.cs:654-665](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L654-L665) | Do not remove the field from `AoeSpawnCommand`; rename it. `PulseVfxFor`/`VfxTimingFor` keep reading it (under the new name) — their behavior is unchanged. |
| The field is part of `AoeSpawnCommand`'s content hash (`SpawnTemplateHash.Of` hashes the raw struct bytes). | [Docs/reference/simulation/spawn-template-registry.md](../../Docs/reference/simulation/spawn-template-registry.md); `SpawnTemplateComponents.cs:201-204` | A field *rename* does not change the hash (hash is over values, not identifiers) and the field is not removed, so template dedup/registry-key behavior is unaffected. Do not remove the field — only rename it. |
| `CombatPoolCleanupSystem` is archetype-agnostic (no gate-specific reset logic anywhere). | Explore-agent finding: zero references to `AoeHitGateComponent`/`RepeatHitCooldownSeconds` in `Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs` | Removing the component requires no changes to pool cleanup logic itself — only to the archetype-construction lists that describe it, and to test helpers that hand-build matching archetypes. |
| Several `AoeSimulationTests.cs` tests assert the cooldown behavior directly by name and premise (`LingeringTargetIsNotRehitUntilTickIntervalExpires`, `LingeringHitsImmediatelyThenRepeatsAfterCooldown`, `LingeringReentryWaitsForNextTickInterval`). | [AoeSimulationTests.cs:478-528](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L478-L528) | These tests' premises no longer exist after this change — delete or fundamentally rewrite them (task 005), not leave them asserting behavior that no longer applies. |
| `Docs/reference/simulation/aoe-system.md` documents `AoeHitGateComponent` as a required component and describes the throttling semantics by name. | [aoe-system.md:181,226-229](../../Docs/reference/simulation/aoe-system.md#L181) | Must be updated in the same change set — a doc describing a deleted component/behavior is worse than no doc. |
| Removing a component changes archetype chunk size, which can shift `EntityQuery`/chunk-capacity assertions. | `AoeSimulationTests.cs`'s `ImpactArchetypeOmitsLingeringOnlyComponentsAndHasLargerChunkCapacity` (~line 609) | Flag for the user to re-run and confirm the expected capacity number after the component is removed — this repo's convention is the agent does not run tests itself (`Docs/testing.md`); the user runs the named suite and reviews the XML. |

## Mechanisms Reused vs. Introduced

**Reused:** nothing new is introduced — this is a pure deletion (component,
its archetype membership, its query requirements, its collision-job
consumption) plus a rename of the one field that survives for an unrelated
purpose (VFX). No new types, no new systems.

## Design Validation

- **Behavior**: lingering AOEs now call `RunCollision` unconditionally every
  tick they're active (`Active` + `CombatCollisionActiveTag` enabled) — this
  is the explicit, confirmed intent.
- **VFX isolation**: `AoePulseVfxSystem`/`AoePulseVfxComponent` and
  `VfxTimingData` are untouched in behavior — they already read the tick
  interval independently of `AoeHitGateComponent`; only the source field's
  name changes.
- **Hash/registry isolation**: confirmed the field rename doesn't perturb
  `AoeSpawnCommand`'s content hash — dedup behavior for AOE templates is
  unchanged.
- **No dangling references**: after tasks 001-003, `AoeHitGateComponent` has
  zero production or test references and can be deleted outright (per this
  project's standing rule that a zero-caller type is deleted, not preserved
  or shimmed — see [[dead-code-is-not-a-constraint]]).
- **Editor-authoring safety**: confirmed `AoeSpawnCommand` is a runtime,
  code-constructed struct, not a serialized asset field — the rename in task
  004 needs no `FormerlySerializedAs`-style migration and doesn't touch
  anything a designer has authored in the Unity Editor. The Inspector-facing
  name (`LingeringAoeDefinition.tickIntervalSeconds`) is already correctly
  named and is untouched.

## Task List

1. [001-remove-gate-from-lingering-collision.md](001-remove-gate-from-lingering-collision.md) — `LingeringAoeCollisionSystem.cs`: drop the query requirement, job handle, and gate-check-then-continue block; collide unconditionally every tick.
2. [002-remove-gate-from-spawn-materialization.md](002-remove-gate-from-spawn-materialization.md) — `AoeSpawnApplySystem.cs`: remove `AoeHitGateComponent` from both archetypes, both jobs' handle wiring, and delete `HitGateFor`; remove it from `ImpactAoeCollisionSystem.cs`'s query too.
3. [003-delete-hit-gate-component.md](003-delete-hit-gate-component.md) — delete the `AoeHitGateComponent` struct from `AoeEcsComponents.cs`; fix up test-helper archetype builders that still reference it.
4. [004-rename-tick-interval-field.md](004-rename-tick-interval-field.md) — rename `AoeSpawnCommand.RepeatHitCooldownSeconds` → `TickIntervalSeconds` across all producers/consumers and tests.
5. [005-rewrite-cadence-tests.md](005-rewrite-cadence-tests.md) — delete/rewrite the three cooldown-premised tests in `AoeSimulationTests.cs`; flag the chunk-capacity test for a user re-run.
6. [006-update-docs.md](006-update-docs.md) — update `Docs/reference/simulation/aoe-system.md`.

## Open Questions

None remaining — both prior ambiguities (whether anything replaces the
cadence, and what happens to the surviving field name) were resolved by the
user in conversation and are captured above.

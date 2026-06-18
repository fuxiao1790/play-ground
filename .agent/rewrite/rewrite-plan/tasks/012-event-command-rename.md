# Task 012: Event/Command naming correction (R3)

## Goal
Make the Event/Command vocabulary match meaning (design R3, D-NAMING-EVENTCMD):
- a type that carries multiplicity (`Count`/`SpreadDegrees`/`JitterDegrees`/`JitterSeed`) is **intent** → `...SpawnEvent`;
- a type that is exactly one resolved entity is **allocation** → `...SpawnCommand` (no `Data` suffix).

This reverses the prior plan's D-NAMING, which kept the managed authoring type called `...SpawnCommand` (even though it carries multiplicity) and suffixed the ECS one-entity type `...CommandData`.

## Required Reading
- `../context/005-decision-log.md` → D-NAMING-EVENTCMD + Governing principle
- `../context/002-target-architecture.md` §1.1–1.3
- `../context/003-data-flow.md` §1–4

## The three current types and their fate
| Current (Increment-1) | Carries | Correct role | New name |
|---|---|---|---|
| managed `ProjectileSpawnCommand` (`System/Projectile/ProjectileSpawnCommand.cs`) — `Count`/`SpreadDegrees`/`JitterDegrees` | multiplicity | **intent** | **`ProjectileSpawnRequest`** (managed authoring DTO) |
| ECS `ProjectileSpawnEvent` (`ProjectileSpawnPipeline.cs`, `IBufferElementData`) | multiplicity | intent | unchanged name (the blittable submission/queue event) |
| ECS `ProjectileSpawnCommandData` | one entity | **allocation** | **`ProjectileSpawnCommand`** |

Same triple for AoE: managed `AoeSpawnCommand` (`System/Aoe/AoeRuntimeEvents.cs`) → **`AoeSpawnRequest`**; ECS `AoeSpawnEvent` unchanged; `AoeSpawnCommandData` → **`AoeSpawnCommand`**.

> DECIDED (no choice for the implementer): three distinct names per domain — `…SpawnRequest` (managed authoring DTO, keeps `UnityEngine.Vector2`/managed snapshots + constructors), `…SpawnEvent` (blittable ECS intent + multiplicity), `…SpawnCommand` (blittable ECS, one entity). `CombatRoot.Spawn(ProjectileSpawnRequest)` converts the request into the blittable `ProjectileSpawnEvent`. "Request" is the managed layer only; the deleted `ProjectileSpawnRequestElement` is unrelated and must not be revived.

## Files To Modify
- `System/Projectile/ProjectileSpawnCommand.cs` — rename the managed type `ProjectileSpawnCommand` → `ProjectileSpawnRequest` (rename the file to `ProjectileSpawnRequest.cs`). Keep its fields/constructors (incl. `Count`/`Spread`/`Jitter`).
- `System/Aoe/AoeRuntimeEvents.cs` — rename managed `AoeSpawnCommand` → `AoeSpawnRequest`.
- `System/Projectile/ProjectileSpawnPipeline.cs`, `System/Aoe/AoeSpawnPipeline.cs` — rename `...SpawnCommandData` → `...SpawnCommand`; ensure the ECS `...SpawnEvent` is the conversion target of `CombatRoot.Spawn`.
- `System/Common/CombatRoot.cs` — `Spawn(...)` parameter type + the authoring→event conversion.
- Call sites: `Mob/MobProjectileAttack.cs`, `Mob/MobRoot.cs`, `Skills/SkillSpawnTranslator.cs`.
- Apply/expansion systems referencing `...CommandData`.
- Tests: `ProjectileSpawnPipelineTests.cs`, `AoeSimulationTests.cs`, authoring/skill EditMode tests, any constructing the managed authoring type.

## Required Changes
1. Rename ECS `ProjectileSpawnCommandData` → `ProjectileSpawnCommand`, `AoeSpawnCommandData` → `AoeSpawnCommand` (the one-entity allocation types).
2. Rename the managed authoring `ProjectileSpawnCommand` → `ProjectileSpawnRequest` and `AoeSpawnCommand` → `AoeSpawnRequest`. Update all call sites.
3. Keep `CombatRoot.Spawn` building the blittable ECS `ProjectileSpawnEvent`/`AoeSpawnEvent` from the authoring intent.
4. Recompile; run.

## Behavior Preservation Requirements
- Pure rename + the existing authoring→event conversion. No field, math, or flow change. The `Count`/`Spread`/`Jitter` fields stay on the **Event** side and are consumed by expansion (D-EXPANSION-OWNS-MATH).

## Dependencies
None (independent of 011; can run in parallel). 013 depends on the new `...SpawnCommand` name.

## Acceptance Criteria
- [ ] No type named `...SpawnCommandData` remains.
- [ ] No multiplicity-carrying type is named `...SpawnCommand`.
- [ ] ECS one-entity types are `ProjectileSpawnCommand` / `AoeSpawnCommand`.
- [ ] Authoring/skill/mob call sites compile against the renamed authoring Event types.
- [ ] Repo compiles; suite green.

## Risk
Medium — touches authoring/skill code. Mechanical, but the managed↔ECS same-name potential is a readability trap; choose distinct, role-correct identifiers and keep the boundary conversion in `CombatRoot`.

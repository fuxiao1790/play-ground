---
name: interval-spawn-typed-config
description: Give the two interval-spawn triggers genuinely type-specific child config (projectile count+spread; echo count+scatter radius)
---

# Interval Spawn Typed Config

## Summary

Keep `ProjectileIntervalSpawnTrigger` and `AoeIntervalSpawnTrigger` as two
distinct trigger types (no merge), but replace their identical generic field
set with fields that are meaningful for each trigger's child type:

| Trigger | Fields (after) |
|---|---|
| `ProjectileIntervalSpawnTrigger` | `intervalSeconds`, `intervalJitterPercent`, `projectileCount`, `sideSpreadDegrees` |
| `AoeIntervalSpawnTrigger` | `intervalSeconds`, `intervalJitterPercent`, `echoCount`, `scatterRadius` |

`projectileCount` / `echoCount` are renames of the generic `spawnCount` and keep
their **additive** semantics (added to the child skill set's own count / echo,
floored to 1). `sideSpreadDegrees` on the projectile trigger is unchanged.
`scatterRadius` on the AOE trigger **replaces the dead `sideSpreadDegrees`** and
becomes the burst scatter radius that actually drives echo placement — it is
threaded through to `AoeSpawnCommand.ScatterRadius`, which the AOE expansion job
already consumes. See [info.md](./info.md) for the full trace.

## Constraints & Invariants

- **ECS/runtime untouched.** No change to `TimedSpawnComponent`,
  `TimedSpawnSystem`, `IntervalChildKind`, materialization, or the expansion
  jobs. `AoeSpawnCommand.ScatterRadius` already exists and is already consumed
  ([AoeSpawnExpansionSystem.cs:53-57](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L53-L57));
  we only change the value baked into it for interval templates. Source:
  [info.md § Unaffected layers](./info.md).
- **Shared template builder must not regress non-interval AOEs.**
  `SkillIntervalTemplateBuilder.BuildAoeTemplate` is also called by the
  top-level / on-hit AOE registration path
  ([PlayerSkillDriver.cs:305-318](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L305-L318)),
  which must keep using `child.ScatterRadius`. The scatter override must default
  to the child's value so only the interval caller supplies a trigger scatter.
- **Set isolation preserved for counts.** `projectileCount` / `echoCount` stay
  additive so the child skill set remains self-contained; the trigger tunes the
  per-tick burst on top. Source: skill-system.md "Set Isolation Rules".
- **Both `TargetSkillTags` unchanged** (`Projectile` / `Aoe` respectively), so
  the validator and its target-tag / pulse-source warnings need no change.

## Mechanisms Reused vs. Introduced

- **Reused**: the existing per-child-type compile methods
  (`ApplyChildSpawn` / `ApplyAoeIntervalSpawn`) and the two runtime setup
  structs stay — we only adjust which fields feed them. The projectile side
  reuses `RuntimeChildSpawnSetup.Behavior` unchanged.
- **Introduced**: one field. `RuntimeAoeIntervalSpawnSetup.ScatterRadius`
  replaces the dead `SideSpreadDegrees`; a scatter override param on
  `BuildAoeTemplate`. No new type, no new data path.

## Design Validation

| Invariant | Held by |
|---|---|
| ECS untouched | Change stops at the main-thread template-build layer; the command field it targets is pre-existing and already consumed |
| Non-interval AOE unaffected | `BuildAoeTemplate` scatter override defaults to `child.ScatterRadius`; only `RegisterAoeIntervalTemplate` passes the trigger-derived value |
| Additive counts | Compiler keeps `Max(1, child.Count + trigger.projectileCount)` / `Max(1, child.EchoCount + trigger.echoCount)` |
| No validator regression | Trigger types and `TargetSkillTags` unchanged |

## Minimal/Additive vs. Refactor Comparison

- **Minimal/additive** (just add `scatterRadius` + rename fields, keep the dead
  `SideSpreadDegrees` around):
  - resulting data flow: one dead field lingers on `RuntimeAoeIntervalSpawnSetup`
    and the AOE trigger; confusing (two placement-ish fields, one inert).
  - new concepts/types: none, but retains dead weight.
  - long-term cost: future readers must rediscover that `SideSpreadDegrees` is
    inert; the "why does the AOE trigger have a spread that does nothing" smell
    persists.
- **Refactor** (this plan — remove `SideSpreadDegrees`, add `ScatterRadius`,
  wire it through):
  - resulting data flow: every field on each trigger is live and type-appropriate.
  - existing types changed: `RuntimeAoeIntervalSpawnSetup` (field swap);
    `BuildAoeTemplate` (scatter override); compiler AOE branch.
  - copies/translations removed: deletes the write to a never-read field.
  - long-term benefit: no inert fields; the AOE trigger's scatter is honest.
  - **Decision: refactor.** The dead field is small but this is the moment it
    is already being touched; leaving it inert would reintroduce the exact
    duplicate-looking smell this task exists to remove.

## Open Decision (flag for review, not blocking)

**Scatter combine semantics.** Counts are additive (confirmed). For the AOE
`scatterRadius`, this plan makes it **authoritative** — the trigger's
`scatterRadius` sets the interval burst scatter directly, ignoring the child
skill's own `ScatterRadius` — to mirror the projectile trigger's
`sideSpreadDegrees`, which is already authoritative (the child's own
`SpreadDegrees` is ignored for interval bursts). This yields one clean rule:
*counts add, geometry is defined by the trigger.* If you'd rather scatter be
additive (`child.ScatterRadius + trigger.scatterRadius`), say so and 002/003
flip a single expression. Called out in [002](./002-runtime-setup-and-compiler.md).

## Task List

1. [001-trigger-fields.md](./001-trigger-fields.md) — rename/replace fields on
   both trigger classes + fix and update the two `.asset` fixtures.
2. [002-runtime-setup-and-compiler.md](./002-runtime-setup-and-compiler.md) —
   swap `RuntimeAoeIntervalSpawnSetup.SideSpreadDegrees` -> `ScatterRadius`;
   update `SkillSetCompiler` field references + scatter compute.
3. [003-template-scatter-threading.md](./003-template-scatter-threading.md) —
   thread `setup.ScatterRadius` through `RegisterAoeIntervalTemplate` ->
   `BuildAoeTemplate` (override defaulting to `child.ScatterRadius`).
4. [004-update-tests.md](./004-update-tests.md) — field-name updates in both
   test files; add AOE `scatterRadius` additive/authoritative assertion; keep
   the two mismatch tests.
5. [005-update-docs.md](./005-update-docs.md) — update `skill-system.md` trigger
   field lists and additive/scatter prose.

## Dependencies

- 001 -> 002 -> 003 form one compile-correctness unit (land together).
- 004, 005 follow. No ECS-layer task; the change stops at the compile/register
  layer by design.

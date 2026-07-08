---
name: interval-spawn-typed-config-exploration
description: Exploration for giving ProjectileIntervalSpawnTrigger / AoeIntervalSpawnTrigger genuinely type-specific child config (projectile count+spread; echo count+scatter)
---

# Exploration Findings

## Direction (supersedes the earlier merge plan)
The earlier idea of collapsing both interval triggers into one generic
`IntervalSpawnTrigger` is **abandoned**. A single generic trigger would have to
surface both projectile fields (count, spread) and AOE fields (echo, scatter)
with roughly half irrelevant for any given child type. Instead: keep the two
triggers and make each carry only fields meaningful for its child type. This is
the "split lane at the producer" shape (see `[[feedback_split_lane_at_producer]]`).

## Current field state (the smell being fixed)
Both triggers currently expose an **identical, generic** field set — the reason
they look like duplicates:
- `ProjectileIntervalSpawnTrigger` ([ProjectileIntervalSpawnTrigger.cs](../../Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs)) — `intervalSeconds`, `intervalJitterPercent`, `spawnCount`, `sideSpreadDegrees`; `TargetSkillTags = Projectile`.
- `AoeIntervalSpawnTrigger` ([AoeIntervalSpawnTrigger.cs](../../Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs)) — same four fields; `TargetSkillTags = Aoe`.

## How each field flows today
- **Projectile child** ([SkillSetCompiler.cs:298-332](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L298-L332)):
  `spawnCount` -> `Behavior.Count = Max(1, child.Count + spawnCount)` (additive);
  `sideSpreadDegrees` -> `Behavior.SpreadDegrees` (**authoritative** — the child
  skill's own `SpreadDegrees` is not consulted for the interval burst). Stored in
  `RuntimeChildSpawnSetup.Behavior` (a `ProjectileChildSpawnBehavior`).
- **AOE child** ([SkillSetCompiler.cs:334-366](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L334-L366)):
  `spawnCount` -> `setup.Count = Max(1, child.EchoCount + spawnCount)` (additive);
  `sideSpreadDegrees` -> `setup.SideSpreadDegrees`. Stored in
  `RuntimeAoeIntervalSpawnSetup`.

## Dead field confirmed
`RuntimeAoeIntervalSpawnSetup.SideSpreadDegrees` ([RuntimeProjectileDefinition.cs:26](../../Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs#L26))
is **written but never read** anywhere downstream (verified: the only reference
is the compiler write at [SkillSetCompiler.cs:342](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L342)).
AOE echo placement is driven entirely by `command.EchoCount` and
`command.ScatterRadius` in the expansion job
([AoeSpawnExpansionSystem.cs:46-57](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L46-L57)).
So the AOE trigger's spread does nothing today; `scatterRadius` is the field that
would actually affect AOE burst geometry.

## Where scatter currently comes from (and must be redirected)
For interval AOE children, the `AoeSpawnCommand.ScatterRadius` is baked from the
**child skill definition**, not the trigger:
`SkillIntervalTemplateBuilder.BuildAoeTemplate(...)` sets
`ScatterRadius = child.ScatterRadius` ([PlayerSkillDriver.cs:722](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L722)).
`RegisterAoeIntervalTemplate` ([PlayerSkillDriver.cs:395-412](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L395-L412))
calls it, passing `Max(1, setup.Count)` as echo count but no scatter. To make the
trigger's `scatterRadius` take effect, it must be carried on
`RuntimeAoeIntervalSpawnSetup` and threaded into `BuildAoeTemplate` in place of /
overriding `child.ScatterRadius`. **Same builder is also used by the top-level /
on-hit AOE registration** ([PlayerSkillDriver.cs:305-318](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L305-L318))
which must keep using `child.ScatterRadius` — so the threading needs an override
that defaults to the child's value, not a signature change that forces every
caller to supply scatter.

## Unaffected layers
- **ECS runtime** — untouched. `TimedSpawnComponent`, `TimedSpawnSystem`,
  `IntervalChildKind`, and the materialization sites key off compiled setup
  shapes, not trigger identity. `AoeSpawnCommand.ScatterRadius` already exists and
  is already consumed; we only change what value gets baked into it for interval
  templates. (Full trace done in the prior exploration turn.)
- **Validator** — `SkillLoadoutValidator` already handles both trigger types via
  `IsIntervalSpawnTrigger` ([SkillLoadoutValidator.cs:194-195](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L194-L195))
  and drives the target-tag / pulse-source warnings off unchanged `TargetSkillTags`.
  Keeping both types means **no validator change**.
- **Projectile runtime setup** — `RuntimeChildSpawnSetup` needs no change; the
  projectile side is a pure authoring-field rename.

## Test impact (both types kept -> fewer deletions than the merge plan)
- `SkillValidationEditModeTests.cs` — the two "projectile-trigger-onto-AOE
  warns/no-ops" tests ([:41-58](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L41-L58),
  [:60-88](../../Assets/Tests/EditMode/SkillValidationEditModeTests.cs#L60-L88))
  **stay valid** (types unchanged). All interval tests need only field-name updates
  (`spawnCount` -> `projectileCount`/`echoCount`); add coverage for AOE
  `scatterRadius`.
- `AoePlayModeTests.cs` — field-name updates on `triggerA.spawnCount` etc.

## Asset fixtures
- `ProjectileIntervalSpawnTrigger.asset` — correct script GUID; update field name
  `spawnCount` -> `projectileCount`.
- `AoeIntervalSpawnTrigger.asset` — script GUID is **wrong** today
  (`a1000000000000000000000000000012` points at `RuntimeAoeDefinition.cs`, not the
  trigger — guid `963edcf1cd09aa84e92b45418222855c`). Since we keep the type, fix
  the GUID and update fields (`spawnCount` -> `echoCount`, `sideSpreadDegrees` ->
  `scatterRadius`). Neither asset is referenced by any loadout (orphaned fixtures).

## Documentation Gaps
- None — `skill-system.md` documents both triggers; the change is field renames +
  one new AOE field, not a structural gap.

# 006 — Strip effect attributes from `IntervalSpawnTrigger`

**Depends on:** nothing in this plan (independent of 001-005; may land before or
after). Shares the plan's governing rule: a trigger says *when*, the triggered
skill set says *what*.
**Scope:** Medium. Four trigger fields removed, three runtime setup fields
removed, two builder parameters removed.

## Goal

`IntervalSpawnTrigger` keeps only cost fields — `energyPerSecond` plus the
`manaCostMultiplier` / `manaCostIncreased` it inherits from `TriggerLink`, and
the two methods that convert between them. Everything describing the child
spawn moves to the child skill set, which already owns all of it.

## The duplication being removed

Each removed field has an exact counterpart already resolved onto the compiled
child, plus a support that already contributes to it:

| Trigger field | Compiler use | Child already owns |
|---|---|---|
| `projectileCount` | `SkillSetCompiler.cs:446` — `Mathf.Max(1, childDef.Count + trigger.projectileCount)` | `RuntimeProjectileDefinition.Count` from `ProjectileDefinition.count` (`:294`), plus `MultipleProjectilesSupport` |
| `sideSpreadDegrees` | `:448` — passed as the behavior's spread, overriding the child | `RuntimeProjectileDefinition.SpreadDegrees` from `ProjectileDefinition.spreadDegrees` (`:296`), plus `MultipleProjectilesSupport` |
| `echoCount` | `:466` and `:485` — added to `childDef.EchoCount` for AOE and targeted children | `RuntimeAoeDefinition.EchoCount` (`:352`), `RuntimeTargetedDefinition.EchoCount` (`:372`), plus `MultipleAoesSupport` / `MultipleChainsSupport` |
| `scatterRadius` | `:467` — overrides the child's scatter | `RuntimeAoeDefinition.ScatterRadius` (`:353`), plus `MultipleAoesSupport` |

The self-spawn path already does exactly what this task makes the interval path
do. Compare `SkillDriver.cs:853-857`, which builds a projectile's own template
as `new ProjectileChildSpawnBehavior(Mathf.Max(1, projDef.Count), selfPattern, projDef.SpreadDegrees)`,
against `SkillSetCompiler.cs:445-448`, which builds the same struct but folds
the trigger in. After this task the two differ only in pattern.

## Edits

### 1. `Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`

Delete `projectileCount`, `sideSpreadDegrees`, `echoCount`, `scatterRadius`.
Keep `energyPerSecond`, `ResolveEnergyPerSecond`, `ManaToEnergyCost`,
`SourceSkillTags`, `TargetSkillTags`. Drop the
`using UnityEngine.Serialization;` and the `[FormerlySerializedAs("count")]` that
went with `echoCount` — check whether any surviving member still needs the
`using` first.

### 2. `Assets/Scripts/Skills/SkillSetCompiler.cs` — `ApplyIntervalSpawn`

Projectile arm (`:445-448`):

```csharp
Behavior = new ProjectileChildSpawnBehavior(
    childDef.Count,
    ProjectileChildSpawnPatternType.SideSpray,
    childDef.SpreadDegrees),
```

`ProjectileChildSpawnBehavior`'s constructor already applies
`Mathf.Max(1, count)` and `Mathf.Max(0f, spreadDegrees)`
(`ProjectileSpawnRequest.cs:142-150`), so the outer `Mathf.Max` goes away with
no loss of the floor. **Keep `SideSpray`** — the pattern is the interval
spawner's own concern, not an effect attribute, and the memory of
"always-SideSpray-from-perpendicular" behavior depends on it.

AOE arm (`:466-467`): delete both `Count` and `ScatterRadius` initializers.
Targeted arm (`:485`): delete the `EchoCount` initializer.

### 3. `Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`

- `RuntimeAoeIntervalSpawnSetup` (`:37-47`): delete `Count` (and its "Per-tick
  burst count" comment) and `ScatterRadius`.
- `RuntimeTargetedIntervalSpawnSetup` (`:27-35`): delete `EchoCount`.
- `RuntimeChildSpawnSetup.Behavior` **stays** — it still carries the SideSpray
  pattern. (It could be dropped and rebuilt in `RegisterProjectileIntervalTemplate`
  from `ChildDefinition`; that is a further simplification, not required here.)

### 4. `Assets/Scripts/Skills/SkillDriver.cs`

- `RegisterAoeIntervalTemplate` (`:911-918`): pass the child's own echo count and
  drop `scatterRadiusOverride:` entirely.
- `RegisterTargetedIntervalTemplate` (`:937`): delete
  `template.EchoCount = Mathf.Max(1, setup.EchoCount);`. `BuildTargetedTemplate`
  already sets `EchoCount = Mathf.Max(1, child.EchoCount)` at `:1539`, so this
  line was overwriting a correct value with a trigger-adjusted one.
- `SkillIntervalTemplateBuilder.BuildAoeTemplate` (`:1455-1462`): remove the
  `float? scatterRadiusOverride = null` parameter and simplify `:1505` to
  `ScatterRadius = Mathf.Max(0f, child.ScatterRadius)`. That parameter existed
  solely for the interval setup — the other caller (`:748`) already passes
  nothing.
- Same signature, the `int echoCount` parameter: after this change both callers
  pass `Mathf.Max(1, child.EchoCount)` (`:750` already does). Remove the
  parameter and read `child.EchoCount` inside, so the template builder has one
  source for it.

### 5. Validator — no change

`ValidateTargetedIntervalEnergyReachability` (`SkillLoadoutValidator.cs:270-290`)
uses only `ResolveEnergyPerSecond` and `ManaToEnergyCost`, both of which survive.

## Behavior changes

The shipped `IntervalSpawnTrigger.asset` sets `manaCostMultiplier: 1.1`,
`manaCostIncreased: 1.1`, `energyPerSecond: 25`, `projectileCount: 1`. The other
three keys are absent, so the C# defaults apply — `sideSpreadDegrees = 30f`,
`echoCount = 0`, `scatterRadius = 0f`.

| Child kind | Before | After |
|---|---|---|
| Projectile | `childCount + 1` projectiles, always fanned at 30° | `childCount` projectiles at the child's own spread |
| AOE | `childEcho + 0` echoes, scatter **forced to 0** | `childEcho` echoes at the child's own `scatterRadius` |
| Targeted | `childEcho + 0` echoes | `childEcho` echoes — identical |

Two real changes: interval projectile waves lose one projectile and take the
child's spread instead of a flat 30°, and an interval AOE child whose set
authors a non-zero `scatterRadius` (or carries `MultipleAoesSupport`) now
actually scatters, where the trigger previously flattened it to 0. Targeted is
unaffected.

Restoring the old feel is authoring work on the child skill set, and there is no
new mechanism for it — `MultipleProjectilesSupport` / `MultipleAoesSupport`
already do exactly this and price the mana correctly.

## Tests

Check `Assets/Tests/` for sites that set any of the four removed fields.
`ProjectileContinuousAuthoringEditModeTests.cs:127-128` sets
`interval.projectileCount = 1`; that line is deleted, and the assertion it feeds
reads `intervalRoot.ChildSpawnSetup.ChildDefinition`, which is unaffected. Sweep
for `sideSpreadDegrees`, `echoCount`, `scatterRadius` in both test assemblies
before assuming that is the only one.

Add one EditMode test —
**`CompilerLeavesIntervalChildBurstToTheChildSet`**: author a projectile child
with `count = 3`, `spreadDegrees = 45f`, wire it through an
`IntervalSpawnTrigger`, and assert `ChildSpawnSetup.Behavior.Count == 3` and
`Behavior.SpreadDegrees == 45f`. Same role as the on-hit test in task 003: it
pins the ownership rule so a future re-addition cannot pass silently.

## Acceptance criteria

1. `IntervalSpawnTrigger` declares only `energyPerSecond` plus its two methods;
   a project-wide search for `projectileCount`, `sideSpreadDegrees`,
   `scatterRadius` and the trigger's `echoCount` returns nothing outside `Docs/`
   and `.agent/`.
2. `RuntimeAoeIntervalSpawnSetup` has no `Count` / `ScatterRadius`;
   `RuntimeTargetedIntervalSpawnSetup` has no `EchoCount`.
3. `BuildAoeTemplate` takes neither `echoCount` nor `scatterRadiusOverride`, and
   both call sites compile.
4. `RegisterTargetedIntervalTemplate` no longer overwrites `template.EchoCount`.
5. An interval-spawned child's `Count` / `SpreadDegrees` / `EchoCount` /
   `ScatterRadius` equal what that set compiles to as a root.
6. The new test exists and passes; existing interval tests pass unchanged after
   the `projectileCount` assignment is dropped.
7. No Unity asset edit is needed — the removed keys simply stop being read.
   `IntervalSpawnTrigger.asset` keeps its stale `projectileCount: 1` line
   harmlessly until the user next saves it; no YAML is hand-edited.

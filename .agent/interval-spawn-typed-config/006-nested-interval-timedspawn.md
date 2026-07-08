---
name: nested-interval-timedspawn
description: Fix interval-child templates dropping the child's own nested TimedSpawn so multi-level interval chains fire
---

# 006 — Nested interval spawners (core fix)

## Problem

A chain where an interval-spawned source is itself an interval spawner does not
fire the second level. Concretely:

```
SetA(proj) → ProjectileIntervalSpawn → SetB(proj) → AoeIntervalSpawn → SetC(aoe)
```

SetB projectiles (spawned by SetA's interval) never spawn SetC AOEs.

## Root cause

Interval-child templates are registered **without** the child's own nested
`TimedSpawn`:
- `RegisterProjectileIntervalTemplate` ([PlayerSkillDriver.cs:376-393](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L376-L393))
  calls `BuildProjectileTemplate(child, setup.Behavior, root, stackEffect, BuildOnHitSpawnRef(child))`
  — the `timedSpawn` parameter defaults to `default`.
- `RegisterAoeIntervalTemplate` ([PlayerSkillDriver.cs:395-412](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L395-L412))
  has the same omission.

By contrast the **top-level** registration passes the nested spawner
(`ProjectileTimedSpawnFromDefinition(projDef)` / `AoeTimedSpawnFromDefinition(aoeDef)`,
[:317](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L317),
[:367](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L367)). So SetB's
*top-level* template is correct, but SetA spawns SetB from SetB's *interval-child*
template, which lacks it.

This is pre-existing and independent of the field-rename tasks (001-005). It is
**not** the value-type recursion limit that blocks `proj→proj→proj` impact
bursts — interval templates carry only a `Hash128 TemplateKey`, so nesting is
representable; it just isn't being baked in.

## Ordering / safety (already satisfied)

- The child's nested setups receive their `TemplateKey`s from the recursive
  `RegisterSpawnTemplatesRecursive(child, depth+1, ...)` call
  ([:326-346](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L326-L346)) that runs
  **before** `RegisterProjectileIntervalTemplate` / `RegisterAoeIntervalTemplate`.
  So `ProjectileTimedSpawnFromDefinition(child)` / `AoeTimedSpawnFromDefinition(child)`
  return an enabled spawner with a valid `TemplateKey` at that point.
- Depth is bounded by `CombatRoot.MaxSpawnChainDepth` ([:255](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L255)),
  which already emits `SpawnChainDepthExceeded` past the limit.
- No ECS job change: the spawned child's `TimedSpawnComponent` is
  enabled from `cmd.HasTimedSpawner`, which `BuildProjectileTemplate` /
  `BuildAoeTemplate` already set from `IsTimedSpawnEnabled(timedSpawn)`
  ([:615](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L615),
  [:633](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L633)).

## Changes

`Assets/Scripts/Skills/PlayerSkillDriver.cs`

### `RegisterProjectileIntervalTemplate` ([:384-390](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L384-L390))
```csharp
ProjectileSpawnCommand template =
    SkillIntervalTemplateBuilder.BuildProjectileTemplate(
        child,
        setup.Behavior,
        combatRoot,
        stackEffect,
        BuildOnHitSpawnRef(child),
        ProjectileTimedSpawnFromDefinition(child),   // ADD nested spawner
        child.JitterDegrees);                        // ADD (top-level passes this too; interval child currently loses authored jitter)
```

### `RegisterAoeIntervalTemplate` ([:403-409](../../Assets/Scripts/Skills/PlayerSkillDriver.cs#L403-L409))
```csharp
AoeSpawnCommand template =
    SkillIntervalTemplateBuilder.BuildAoeTemplate(
        child,
        Mathf.Max(1, setup.Count),
        combatRoot,
        stackEffect,
        BuildOnHitSpawnRef(child),
        AoeTimedSpawnFromDefinition(child),          // ADD nested spawner
        scatterRadiusOverride: setup.ScatterRadius); // from task 003
```

## Interaction with task 003

003 adds the trailing `scatterRadiusOverride` param to `BuildAoeTemplate`. This
task adds the `timedSpawn` positional arg (the param *before* it). Land them
together in the `RegisterAoeIntervalTemplate` call so both new args are supplied
(use named `scatterRadiusOverride:` as shown). If 006 lands before 003, pass
only `AoeTimedSpawnFromDefinition(child)` and add the scatter arg in 003.

## Acceptance Criteria

- `SetA(proj) → projInterval → SetB(proj) → aoeInterval → SetC(aoe)`: SetB
  projectiles spawn SetC AOEs at interval. (User verifies via a PlayMode test —
  harness cannot run Unity.)
- Symmetric nestings also fire: proj→proj→proj, proj→aoe(lingering)→aoe,
  aoe(lingering)→proj→aoe, etc., up to `MaxSpawnChainDepth`.
- Single-level interval behavior unchanged.
- Interval-spawned projectiles now carry their authored `JitterDegrees`
  (previously lost). If the reviewer wants a minimal diff, the `child.JitterDegrees`
  addition can be dropped independently of the `timedSpawn` fix.

## Suggested PlayMode coverage

Extend `AoePlayModeTests` with a two-level interval chain asserting SetC AOE
entities appear while SetA is alive (mirror
`LingeringAoeIntervalChildrenUseRegistryTemplatesUntilSourceExpires`, but with an
interval-projectile middle layer that carries an AOE interval spawner).

## Dependencies

Independent of 001-005 (fixes a pre-existing gap). Only interacts with 003 via
the shared `BuildAoeTemplate` call site (see above). This is the functional
priority — 001-005 are field ergonomics; 006 makes nested interval chains work.

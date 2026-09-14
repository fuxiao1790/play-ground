# Skill Gameplay System

All docs in `Docs/` are design references. Check code before locking in an
implementation.

This document owns player-facing skill authoring and orchestration. For copied
runtime data, ECS spawn handling, pooling, and simulation, see
[Skill ECS Simulation](../simulation/skill-ecs-simulation.md).

## Scope

Game logic owns what a player can author, equip, modify, trigger, and cast. It
produces immutable-at-use plain-data snapshots. It does not allocate or inspect
in-flight combat entities.

| Game logic owns | Game logic does not own |
|---|---|
| Skills, supports, skill sets, loadouts, trigger links, and validation | ECS entities, components, pools, movement, collision, or lifetime |
| Stat resolution, compilation, cooldown state, and root-cast intent | ECS spawn event expansion, commands, and apply systems |
| Player-facing mana cost and trigger-chain semantics | Target proxy queries, hit qualification, damage application, or VFX dispatch |

Authoritative layer rules: [Game Logic](../../layers/game-logic.md),
[ECS Simulation](../../layers/ecs-simulation.md), and
[Game Logic And Simulation Boundary](../architecture/game-logic-simulation-boundary.md).

## Loadout Authority

Runtime loadouts use normalized ordered nodes: `SkillLoadoutNode.skillSet` plus
`triggerToNext`. `SkillDriver` owns mutable session clone and commits edits.
UI sends commands; UI never owns equipment state. See
[Skill Loadout Editing](../../contracts/skill-loadout-editing.md) and
[Skill Loadout Edit](../../flows/skill-loadout-edit.md).

Alternating managed-reference slot guidance in
[skill-system.md](./skill-system.md) is historical only. Do not use it for new
authoring or implementation.

## Authoring Model

**Skill** is immutable ScriptableObject baseline for one attack. It owns base
rate, typed definition, visual/collision preset reference, and behavior values.

**Stat Modifier Support** modifies only skill in same `SkillSet`, through typed
flat, increased, multiplier, or behavior interfaces. It cannot affect another
set. See [Skill Modifiers](./skill-modifiers.md).

**Stacking Support** converts its set to triggered-only stack detonation. It
owns threshold, lifetime, stacks-per-hit, and presentation configuration.

**Skill Set** is one skill plus zero or more supports. It has no knowledge of
incoming or outgoing triggers, except conversion support can make it
triggered-only.

**Trigger Link** belongs to loadout. It connects source node to next target
node, and owns condition plus trigger-specific parameters.

**Skill Loadout** owns root nodes, trigger links, and root input bindings.

### Skill Types

| Skill asset | Authored definition | Gameplay meaning |
|---|---|---|
| `ProjectileSkill` | `ProjectileDefinition` | Moving attack; may include count, pierce, or tracking behavior. |
| `AoeSkill` | `AoeDefinition` | Immediate pulse area. |
| `LingeringAoeSkill` | `LingeringAoeDefinition` | Area with lifetime and tick interval. |
| `TargetedSkill` | `TargetedDefinition` | Target-proxy chain. |

Skill tags validate support compatibility. Incompatible placement remains
allowed for editing, compiles as no-op, and yields a validation warning.

### Definition Ownership

Prefab references supply authored visual and collision presets. Definition
behavior fields supply player-facing behavior: damage, rate, speed, duration,
count, area size, chain values, mana cost, and direct-damage enablement.

`SkillDefinition.DeepCopy()` creates mutable compile input. Authoring
ScriptableObjects never mutate at runtime.

`manaCost` is authored on every skill definition and folds through
`SkillStat.ManaCost`. Mana supports use normal
`(base + added) * increased * multiplier` fold. See
[Numeric Modifiers](../architecture/numeric-modifiers.md) and
[Skill Modifiers](./skill-modifiers.md).

### Targeted Skill Semantics

`echoCount` is independent chains per cast. `chainCount` is links per chain.
`chainDistance` is every-hop reach, including first link. `chainDelay` is delay
between links; `0` resolves walk in one update. Only immediately previous
target is excluded. Earlier target can recur. Damage falloff applies before
crit roll.

Targeted authoring has no lifetime. Gameplay derives fail-safe bound from
`chainCount * chainDelay` plus margin. Simulation owns actual entity lifetime;
see [Targeted System](../simulation/targeted-system.md).

## Stat Resolution And Compilation

`SkillStatAggregator` folds player-level sources into `SkillStatSnapshot`.
`SkillSetCompiler` deep-copies definition, collects compatible support
modifiers/behavior, applies conversion supports, then produces a
`RuntimeSkillDefinition` tree. This rebakes on equipment, buff, level, or
loadout change; no per-frame support stat math.

`SkillDriver` owns root `SkillSlotState` cooldowns. It derives recovery as
`1 / rate`, gates player input, recompiles changed paths, and asks
`SkillSpawnTranslator` to create root-cast intent.

Compilation output is a plain runtime snapshot. It may contain resolved type
ids and template keys, but it cannot contain live authoring references for
simulation to read. Snapshot and bridge rules live in
[Skill Runtime Snapshots](../../contracts/skill-runtime-snapshots.md) and
[Skill ECS Simulation](../simulation/skill-ecs-simulation.md).

## Trigger Semantics

Trigger links affect only adjacent source and target nodes. Triggered sets use
their own authored skill and supports; source stats never inherit into target.
One set can be effect of several links and compiles once per path. A recursive
self-reference creates separate runtime instances and compiler stops outgoing
recursion for inner instance.

Supported trigger meanings:

- `OnHit`: source hit fires target effect.
- `IntervalSpawn`: active projectile or lingering AOE rolls deterministic
  starting charge from negative to positive authored `initialEnergyPercent`
  (Inspector slider, `0..100`) of child threshold. Negative charge delays first
  child; positive charge advances it. Future gain is stat-folded; starting
  charge is not energy-gain stat input.
- `StackTrigger`: applicator writes target stacks; threshold fires configured
  detonation.

### Projectile Launch Aim

A trigger link may additionally author a projectile launch-aim policy:
`ProjectileLaunchAimMode` (`None` or `NearestHostile`) plus a non-negative
acquisition range. This governs how the *triggered* projectile effect is
launched, not the trigger's own targeting or cost — root/player casts always
use manual aim or existing player aim assist; launch-aim policy applies only to
projectile effects reached through an incoming trigger edge, and only when the
compiled target is itself a projectile. Values are inert for AOE/targeted
targets and for the top-level/root compiled projectile.

Launch aim is independent of discrete-only homing (`Tracking`/`trackingEnabled`
on `ProjectileDefinition`): a triggered projectile may enable one, the other,
both, or neither. On successful acquisition, launch aim recomputes the whole
wave as one full radial nova oriented from the acquired direction — shot `0`
points exactly at the target, and every other shot is spaced evenly (`360`
degrees divided by shot count) around it — applied once at spawn, not steered
per frame; homing, by contrast, is continuous re-steering after spawn, and only
the discrete projectile archetype carries a tracking component at all. Failed
or disabled acquisition leaves the trigger's normally authored pattern
untouched. See [Projectile System](../simulation/projectile-system.md#launch-aim)
for the exact rotation formula and fallback conditions.

Root cast spends mana once for complete valid trigger chain. Each link resolves
its own increased/multiplier factor. For active `A` and triggered `T1`, `T2`:

```text
(A * factor1 * factor2) + (T1 * factor1) + (T2 * factor2)
```

Internal triggered effects never spend mana again. Interval energy threshold
uses child resolved mana cost; chain aggregation does not change interval
timing.

## Root Cast And Rejection

Root cast supplies compiled snapshot, caster proxy, mana cost, and cast token.
`SkillDriver` resets cooldown only when cast enters bridge; failed resource gate
returns token and driver refunds matching cooldown. Gameplay owns cooldown and
player feedback. ECS owns mana mutation, rejection, and later internal child
spawns. See [Resource Spend Gate](../../flows/resource-spend-gate.md) and
[Skill ECS Simulation](../simulation/skill-ecs-simulation.md#root-cast-gate).

## Validation And Invariants

- One set's supports never modify another set.
- Trigger links do not transfer stats or behavior.
- Root and triggered skills compile from their own definitions.
- Player-facing APIs expose skills, supports, sets, loadouts, and triggers;
  never `CombatRoot` or ECS systems.
- Invalid links/supports warn and compile as no-op where possible.
- Simulation never reads live ScriptableObjects, prefabs, or managed target
  callbacks.

## Related Docs

- [Skill Modifiers](./skill-modifiers.md)
- [Skill ECS Simulation](../simulation/skill-ecs-simulation.md)
- [Skill To Combat Spawn](../../flows/skill-to-combat-spawn.md)
- [Spawn Requests](../../contracts/spawn-requests.md)
- [Skill Runtime Snapshots](../../contracts/skill-runtime-snapshots.md)
- [Simulation ECS Overview](../simulation/index.md)

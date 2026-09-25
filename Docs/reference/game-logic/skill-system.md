# Skill System

> Legacy combined reference. Current documentation split by layer:
> [Skill Gameplay System](./skill-gameplay-system.md) for player-facing
> authoring and orchestration; [Skill ECS Simulation](../simulation/skill-ecs-simulation.md)
> for copied runtime data and ECS behavior. Do not add new material here.

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Current Loadout Editing Authority

The target runtime loadout uses normalized ordered nodes (`skillSet` plus
`triggerToNext`), not alternating managed-reference slot entries. The mutable
session clone belongs to `SkillDriver`; UI sends commands and never owns
equipment state. See the authoritative
[Skill Loadout Editing contract](../../contracts/skill-loadout-editing.md),
[Skill Loadout Edit flow](../../flows/skill-loadout-edit.md), and
[Player Skill UI](../design/player-skill-ui.md). The later alternating-slot
sections are historical migration notes only and must not be used for new
authoring or implementation.

## Concepts

**Skill** 鈥?an active spell or attack. Defines what is spawned: a projectile,
an AOE, a targeted chain, a beam. Owns base visual, collision shape, base rate (attacks/casts
per second), and behavior data. A Skill slotted alone fires with base behavior and no
augmentation.

**Stat Modifier Support** - augments the Skill in the same set through typed
modifier kinds: flat base additions, increased percentages, multipliers, or
behavior contexts. Only ever affects the one Skill it shares a set with. No
cross-set influence. See
[skill-modifiers.md](./skill-modifiers.md) for the modifier interfaces.

**Skill Set** 鈥?the unit of authoring. Contains one or more Skills and zero or
more Skill Supports. Self-contained: its Skills are only modified by its own
Supports. Skill Sets have no knowledge of what triggers them or what they
trigger.

**Trigger Link** 鈥?external wiring between two Skill Sets. Owned by the
loadout, not by either set. Defines the source set whose events fire the
trigger, the target set that fires when triggered, the trigger condition, and
any trigger-specific parameters.

**Skill Loadout** 鈥?owns root Skill Sets (those fired by player input), all
Trigger Links between sets, and the input bindings for root sets.

---

## Architecture

The skill system drives the combat runtime without knowing about `CombatRoot`
or any internal config types. A translation layer sits between the two.
The skill system only crosses the boundary through that layer. The player-facing
authoring surface is limited to skill system types.

For the broader ownership split between skill/game logic docs and simulation
docs, see
[game-logic.md](../../layers/game-logic.md) and
[ecs-simulation.md](../../layers/ecs-simulation.md).

```
+----------------------------------+
| Layer 1: Equipment state         |
| Skill, SkillSupport,             |
| SkillSet, TriggerLink,           |
| SkillLoadout                     |
|                                  |
| Mutable. Live equipment state.   |
| Uses Layer 1.5 to produce        |
| SkillStatSnapshot on change.     |
+----------------------------------+
                  |
                  v
+----------------------------------+
| Layer 1.5: Stat resolution       |
| SkillStatSnapshot + SkillSet     |
+----------------------------------+
                  |
                  v
+----------------------------------+
| Layer 2 (orchestration layer)    |
| SkillDriver                      |
| SkillSlotState[] (cooldowns)     |
| RuntimeSkillDefinition[]         |
|                                  |
| Owns runtime slot state.         |
| Gates input to internals.        |
| Uses Layer 2.5 to dispatch.      |
+----------------------------------+
                  |
                drives
                  |
                  v
+----------------------------------+
| Combat runtime (internal)        |
| CombatRoot, ECS systems,         |
| MonoBehaviours                   |
+----------------------------------+
Layer 1.5 (SkillStatAggregator) - stateless utility used by Layer 1. Not a chain tier.
Layer 2.5 (SkillSpawnTranslator) - stateless utility used by Layer 2. Not a chain tier.

```

### Layer 1: Equipment State

Types: `Skill`, `StatModifierSupport`, `SkillSet`,
`TriggerLink`, `SkillLoadout`

- Owns all stat sources: base skill stats, base rate, supports, items,
  buffs, character level
- `SkillLoadout` is live mutable equipment state 鈥?not just an authoring template
- `Skill` SOs are immutable authored templates; `SkillLoadout` holds mutable slot references into them
- Save/load serializes slot references, not compiled runtime trees
- Emits change events when equipment or stat sources change (consumed by Layer 1.5)
- No per-frame stat math; scaling is rebaked on equipment swaps, buff changes, level up, etc.

### Layer 1.5: Stat Resolution

Types: `SkillStatSnapshot`, `SkillStatAggregator`

Stateless utility - not a chain tier. Pure aggregation function: inputs in, flat snapshot
out, no stored state. Collapses player-level Layer 1 stat sources into a flat
snapshot that feeds the same per-stat fold used by supports. Owns no data -
all sources come from Layer 1. See
[skill-modifiers.md](./skill-modifiers.md#player-level-stat-resolution-layer-15)
for `SkillStatSnapshot` fields and baking rules, and
[numeric-modifiers.md](../architecture/numeric-modifiers.md) for the fold
formula itself.

### Layer 2: Orchestration

Types: `SkillDriver`, `SkillSlotState`, `SkillSetCompiler`

- Reads the flat `SkillStatSnapshot` from Layer 1.5 鈥?does not compute stats
- Compiles `SkillSet` + snapshot into `RuntimeSkillDefinition` trees whenever stats change
- After compile: registers `BasicAttackPrefab` templates and `AoeTypeDefinition`
  entries with the bound `CombatRoot`; stores resolved type IDs into compiled
  definitions. Re-registration on equip change is safe 鈥?`CombatRoot` deduplicates by reference.
- Registers energy-driven child-spawn templates with the bound `CombatRoot`; compiled
  setup stores only the returned content-hash `TemplateKey` plus its per-source
  energy configuration.
- Owns `SkillSlotState` per root slot: tracks cooldown elapsed time, gates input-driven casts
- On player input: checks slot cooldown; if ready, calls `SkillSpawnTranslator` and resets timer
- On Layer 1 change: recompiles affected paths, re-registers types, updates slot `recoveryTime`
- Recursively registers each compiled definition's prefab-authored spawn clip
  with `AudioRoot` and stores the stable id in `SkillSoundIds.SpawnId`.
- Holds scene-side references to `CombatRoot`, `CombatVfxRoot`, and `AudioRoot`
  鈥?internal wiring only

`SkillSlotState` per root slot:
- `elapsedSinceLastFire` 鈥?ticked each frame, reset on successful fire
- `recoveryTime` 鈥?copied from `RuntimeSkillDefinition` whenever stats change;
  derived internally as `1 / rate`, not authored directly
- `IsReady` 鈥?`elapsedSinceLastFire >= recoveryTime`
- `CooldownProgress` 鈥?0..1, for UI

### Layer 2.5: Translation

Types: `SkillSpawnTranslator`

Stateless utility 鈥?not a chain tier. `CombatRoot` requires visual types to be
pre-registered before any spawn; it bakes sprite mesh, material, and VFX
handlers at registration time and returns a type ID used in spawn commands.
IDs are generated inside `CombatRoot`; it deduplicates (re-registering same
definition returns the existing ID). Type ID resolution is state 鈥?it belongs
in Layer 2, not here.

`SkillSpawnTranslator` takes a `RuntimeSkillDefinition` with type IDs already resolved
by Layer 2 + origin + aim; resolves AOE spawn geometry (logical collision size
and visual sprite scale) before submitting spawn requests to `CombatRoot`;
returns nothing.

---

## Skill Set Structure

```csharp
class SkillSet : ScriptableObject {
    Skill skill;
    SkillSupport[] supports;
}
```

A set with no supports fires the Skill at base values. A set is fully
self-contained 鈥?it does not reference other sets and carries no trigger data.

---

## Skills

Skills are ScriptableObjects that carry the authored baseline for one type of
attack. The baseline is a definition tree of plain serializable C# classes
containing visual, collision, and behavior data. Skills do not own augmentation
鈥?that belongs to Supports.

`Skill` is abstract. Concrete types are `ProjectileSkill`, regular `AoeSkill`,
`LingeringAoeSkill`, and `TargetedSkill`, each holding their typed definition
inline. Regular and lingering AOEs derive from the same AOE skill base; targeted
skills derive from the targeted skill base. There is only one targeted type: a
chain's lifetime is its walk, so there is nothing for a lingering variant to
mean. The SO is never mutated at runtime.

```csharp
abstract class Skill : ScriptableObject {
    float baseRate;                  // casts/sec; folded through the Rate stat at compile time
    public abstract SkillDefinitionTags Tags { get; }
    public abstract SkillDefinition Definition { get; }
}

[CreateAssetMenu(menuName = "PlayGround/Skills/Projectile Skill")]
sealed class ProjectileSkill : Skill {
    ProjectileDefinition definition;
}

[CreateAssetMenu(menuName = "PlayGround/Skills/AOE Skill")]
sealed class AoeSkill : AoeSkillBase {
    AoeDefinition definition;
}

[CreateAssetMenu(menuName = "PlayGround/Skills/Lingering AOE Skill")]
sealed class LingeringAoeSkill : AoeSkillBase {
    LingeringAoeDefinition definition;
}

[CreateAssetMenu(menuName = "PlayGround/Skills/Targeted Skill")]
sealed class TargetedSkill : TargetedSkillBase {
    TargetedDefinition definition;
}

```

Skill tags are runtime-authoring metadata used for validation, not hard gates.
Current tags:

| Tag | Meaning |
|---|---|
| `Projectile` | Skill compiles to a projectile runtime definition |
| `Aoe` | Skill compiles to an AOE runtime definition |
| `Targeted` | Skill compiles to a target-proxy chain runtime definition |

Players may still place any support or trigger beside any skill. Incompatible
links and supports compile as no-ops and return validation warnings for UI.

`SkillDefinition` is an abstract serializable base class with a `DeepCopy()`
method. Concrete definitions are copied at compile time into a mutable runtime
instance. The SO template is never mutated.

Current Skill types and their definition roots:

| Skill type | SO type | Definition root |
|---|---|---|
| Projectile skill | `ProjectileSkill` | `ProjectileDefinition` |
| AOE skill | `AoeSkill` | `AoeDefinition` |
| Lingering AOE skill | `LingeringAoeSkill` | `LingeringAoeDefinition` |
| Targeted skill | `TargetedSkill` | `TargetedDefinition` |

### Prefab Sound Authoring

Designers assign `spawnSound` and `spawnSoundRadius` on `BasicAttackPrefab`,
`BasicAoePrefab`, `LingeringAoePrefab`, or `TargetedPrefab`, beside the prefab's
visual/VFX authoring. Clips do not live on the `Skill` ScriptableObject.

Compilation passes the prefab values through the typed definition and
`SkillSetCompiler` into `RuntimeSkillDefinition.SpawnSound` and
`SpawnSoundRadius`. `SkillDriver.CompileAndRegister` recursively registers clips
with `AudioRoot` and stores `SkillSoundIds.SpawnId` on each definition. Spawn
templates carry the id and radius into ECS; expansion emits one occurrence for
every materialized root or nested spawn.

`BasicAttackPrefab` also has an optional trail VFX slot: a `VisualEffectAsset`,
`VfxDataShape` (default `LineSegment`), width, and step distance. `SkillDriver`
registers that slot and stores the resolved id, width, and step distance on
skill-built projectile commands. Step distance paces segments by travel distance,
like `TrailRenderer.minVertexDistance`, not by frame. AOE and targeted prefabs
have other VFX slots, but do not have hit, expire, pulse, or arming sound slots. See
[Sound Events](../../contracts/sound-events.md) for runtime timing, selection,
and listener rules.

### ProjectileDefinition

```
ProjectileDefinition
 鈹溾攢 prefab:    BasicAttackPrefab   鈫?sprite, material, hitbox collider, particle effects
 鈹斺攢 behavior:  speed, lifetime, damage, count, spreadDegrees, jitterDegrees,
               pierceCount, repeatHitCooldown,
               trackingEnabled, trackingRange, trackingTurnSpeed,
               trackingQueryInterval, trackingInitialDelay,
               manaCost, directDamageEnabled
```

The prefab is a visual and collision preset. The runtime bakes collision shape
and render data from it at load time, identical to current baking behavior.
Behavior fields on the definition drive simulation 鈥?the prefab contributes
nothing to behavior.

`manaCost` belongs to every projectile, AOE, or targeted definition. It folds through
`SkillStat.ManaCost` during runtime compilation, so compatible supports can
modify it - see [skill-modifiers.md](./skill-modifiers.md) for the fold and
the supports that contribute to it. A player-cast skill spends once, using
the complete valid trigger chain. Every `TriggerLink` resolves its own
mana-cost factor from `manaCostIncreased` and `manaCostMultiplier` -
see
[skill-modifiers.md#mana-cost-factor-trigger-links](./skill-modifiers.md#mana-cost-factor-trigger-links).
The resolved factor is used for the interval child-energy threshold, the
initial active skill chain cost, and that link's triggered skill cost.

For active skill `A` and two triggered skills `T1`, `T2`, the cost is:

```text
(A * multiplier1 * multiplier2)
+ (T1 * multiplier1)
+ (T2 * multiplier2)
```

Here each `multiplierN` is that link's resolved factor, not its raw
`manaCostMultiplier` field.

Internal triggered spawns never spend mana again. Interval triggers still
convert the child skill's own resolved mana cost into an energy threshold, so
chain-cost aggregation does not alter interval timing.

### AoeDefinition

```
AoeDefinition
 鈹溾攢 prefab:    BasicAoePrefab   鈫?sprite, material, hitbox collider, particle effects
 鈹斺攢 behavior:  baseAreaSize, damage, echoCount, scatterRadius,
               manaCost, directDamageEnabled
```

Regular AOE content compiles as pulse AOE. It does not expose lifetime or tick
interval, and the runtime receives `0` for both timing fields.

### LingeringAoeDefinition

```
LingeringAoeDefinition
 鈹溾攢 prefab:    LingeringAoePrefab 鈫?sprite, material, hitbox collider, particle effects
 鈹斺攢 behavior:  baseAreaSize, damage, lifetimeSeconds, tickIntervalSeconds,
               echoCount, scatterRadius, manaCost, directDamageEnabled
```

### TargetedDefinition

```
TargetedDefinition
 - prefab:    TargetedPrefab       - visual and VFX preset only; no Hurtbox
 - behavior:  damage, echoCount, chainCount, chainDistance, chainDelay,
              chainDamageFalloff, armSeconds,
              manaCost, directDamageEnabled
```

| Field | Meaning |
|---|---|
| `echoCount` | Independent chains created by one cast. |
| `chainCount` | Links walked by **one** chain. |
| `chainDistance` | Reach of every hop, link 0 included. |
| `chainDelay` | Seconds between links; `0` resolves the whole walk in one update. |

Targeted chains do not use Physics2D or a gameplay collider. On each walk the
first link searches from the cast `acquireAnchor`; later links search from the
last hit target, both within `chainDistance`. They resolve against target
proxies through the spatial hash. Only the immediately previous target is
excluded, so earlier targets may be visited again. `chainDamageFalloff` is
applied as `falloff^linkIndex`, before the hit's crit roll.

**There is one targeted variant, and it authors no lifetime.** The instance
expires the moment it uses its last chain, or the moment a link finds nothing.
The compiler derives a fail-safe `CombatLifetimeComponent` from
`chainCount * chainDelay` plus a margin purely so a stalled instance cannot leak
a pooled slot. A chain that repeats over time is composed instead: put a
`IntervalSpawnTrigger` on a projectile or lingering AOE source.

### Unit Resources and Root Casts

`UnitStatSheet` authors maximum and regen values for health and mana. Every unit
root holds the same managed `Resource` mirror; it seeds `Health` and `Mana` at
proxy creation, pushes only Max/regen changes, and reads ECS-owned `Current` back
for presentation. `ResourceRegenSystem` regenerates live ECS resources after
combat application. Health does not regenerate a depleted unit back to life.

Root casts submit an `ExternalSpawnRequest` carrying the compiled root skill's
`ManaCost`, caster proxy, and cast token. The serial `ExternalSpawnGateSystem`
deducts `Mana` then emits the usual internal spawn event, or emits a rejection.
`SkillDriver` refunds the matching cooldown on rejection. Internal interval,
on-hit, and hit-energy-activated child spawns never spend mana again.

### HitEnergyTrigger

`HitEnergyTrigger` connects an adjacent projectile, AOE, or targeted source to
a projectile or AOE output. Compilation attaches a `RuntimeHitEnergyTrigger` to
the source definition; this is edge composition, not a
`RuntimeSkillDefinition` subtype.

Each accepted source hit carries a `HitEnergyPayload` with copied primitive
values. Effective values use one positive floor:

```text
EnergyPerHit = max(1e-3, source.TriggerEnergy * energyContributionMultiplier)
EnergyRequired = max(1e-3, triggered.TriggerEnergy * energyRequirementMultiplier)
```

`CombatApplyFinalizeSingleSystem` deposits `EnergyPerHit` into target-local
`TargetHitEnergy`, refreshes expiry from `retentionSeconds`, and keeps entries
keyed by `AccumulatorId`. Every compiled edge receives a distinct id independent
of asset identity, equal values, runtime type, or template key. Only an adjacent
source funds its next node; repeated use of the same Skill or SkillSet asset
still produces isolated runtime nodes and edge accumulators.

`HitEnergyActivationSystem` runs before current-update hit finalization. It
expires stale entries, emits one output per complete `EnergyRequired`, preserves
float remainder and overflow beyond the 256-per-target/update cap, and uses the
registered `HitEnergySpawn` template reference as the sole source of output
damage, area, count, crit, launch, and descendant behavior. Current-update
deposits therefore activate no earlier than the next simulation update.

Hit energy is distinct from time-based interval energy. It does not change
`IntervalSpawnTrigger` charge, thresholds, or mana behavior.

---

## Supports

Each support derives from `SkillSupport`. Augment supports derive from
`StatModifierSupport`, which carries `SupportedSkillTags` for validation, and
then implement one or more stat-modifier kind interfaces. Hit-energy behavior
is owned by `HitEnergyTrigger`, not by a support.

See [skill-modifiers.md](./skill-modifiers.md) for the modifier kind
interfaces, the current augment support roster, and their compatible skill
tags. The numeric fold those interfaces feed is a general pattern, not a
skill-system concept - see
[numeric-modifiers.md](../architecture/numeric-modifiers.md).

---

## Legacy Alternating Slot Model (Historical)

The `SkillLoadout` is a flat ordered list of `LoadoutSlot` entries. Each slot
is either a `SkillSetSlot` or a `TriggerLinkSlot`. Position determines
relationship 鈥?no explicit source/target references exist.

```csharp
abstract class LoadoutSlot { }

class SkillSetSlot : LoadoutSlot {
    SkillSet skillSet;
}

class TriggerLinkSlot : LoadoutSlot {
    [SerializeReference] TriggerLink link;
}
```

`SkillLoadout.slots` uses `[SerializeReference]` so polymorphic `TriggerLink`
instances inside `TriggerLinkSlot` serialize correctly in Unity.

### Parsing Rule

At compile time, `SkillDriver` scans the slot list. At every
`SkillSetSlot` at index `i`, if `slots[i+1]` is a `TriggerLinkSlot` and
`slots[i+2]` is a `SkillSetSlot`, those three form a `TriggerChain`:

```
cause  = slots[i].skillSet       鈫?left: always the trigger source
link   = slots[i+1].link         鈫?trigger type and parameters
effect = slots[i+2].skillSet     鈫?right: always the trigger target
```

`TriggerChain` is a runtime-only struct 鈥?it is never serialized.

### Root Detection

A `SkillSetSlot` is a **root** (fired by player input) if its skill set does not
appear as an `effect` in any parsed chain. All other skill sets are triggered.

```
slots: [SetA | ProjectileIntervalSpawn | SetB | OnHit | SetC]

chains:  SetA 鈫?ProjectileIntervalSpawn 鈫?SetB
         SetB 鈫?OnHit 鈫?SetC

effects: {SetB, SetC}
roots:   {SetA}         鈫?SetA is the only player-input slot
```

### Trigger Types

`TriggerLink` owns link UI and mana-cost factors. A trigger says **when** an
effect fires; the effect skill set says **what** fires; `TriggerLink` prices
the link. The cause/effect relationship is implicit from slot position, not
stored on the link.

```csharp
abstract class TriggerLink {
    // UI fields, manaCostMultiplier, manaCostIncreased
}
```

**IntervalSpawnTrigger**

The cause duration skill accrues energy and spawns children from the effect set
whenever it reaches the child cost. One trigger asset supports every spawnable
effect shape:

```csharp
class IntervalSpawnTrigger : TriggerLink {
    float energyPerSecond;
}
```

The child skill set owns projectile count/spread, AOE echo count/scatter, and
targeted echo count. Its definition and supports resolve these values before
the interval setup is built.

`SourceSkillTags = Interval`. `SkillDefinitionTags.Interval` marks skill shapes
with a duration that can accrue energy: `ProjectileSkill` and
`LingeringAoeSkill`. Pulse `AoeSkill` and `TargetedSkill` do not carry this tag,
so the generic `UnsupportedTriggerSource` tag mismatch reports an Error and the
compiler produces no timed-child setup. `TargetSkillTags = Any`, so no effect
shape is invalid merely because of an authored trigger variant.

The compiler chooses setup from the compiled effect runtime type, not from the
trigger asset: `RuntimeProjectileDefinition` builds `RuntimeChildSpawnSetup`,
`RuntimeAoeDefinition` builds `RuntimeAoeIntervalSpawnSetup`, and
`RuntimeTargetedDefinition` builds `RuntimeTargetedIntervalSpawnSetup`. Each is
assigned to the matching field on the projectile or lingering-AOE source.

Energy-driven source/child support:

| Source / Child | Any child type |
|---|---|
| Projectile source | `IntervalSpawnTrigger` |
| Lingering AOE source | `IntervalSpawnTrigger` |
| Pulse AOE or targeted source | error, no-op |

A targeted chain is never an interval **source**: it has no duration of its own
to accrue energy over. It is only ever a child.

For a targeted effect, `IntervalSpawnTrigger` starts targeted-chain children
whenever it reaches the child cost. **This is how a chain repeats over time** —
the targeted definition itself has no tick interval. The child definition's own
`echoCount`, floored to one during compilation, is the number of chains.

`IntervalSpawnTrigger` carries `energyPerSecond` and inherits mana-cost fields
from `TriggerLink`. The child
definition owns `manaCost`; its compiled, support-folded `ManaCost` is
converted by `IntervalSpawnTrigger.ManaToEnergyCost`. The baked threshold is
`max(0.001, childManaCost * ResolveManaCostFactor())`.

The compiled `EnergyPerSecond` is not the trigger's raw authored value.
`IntervalSpawnTrigger.ResolveEnergyPerSecond` folds it against the player
stat sheet's energy-gain terms:

```text
Mathf.Max(0.01f, StatFold.Resolve(
    energyPerSecond,
    snapshot.BaseEnergyGain,
    snapshot.IncreasedEnergyGain,
    snapshot.EnergyGainMultiplier))
```

`baseEnergyGain`, `increasedEnergyGain`, and `energyGainMultiplier`
are authored on `UnitStatSheet` and reach every interval trigger through the
same `SkillStatSnapshot` used for Rate/Damage/AreaSize. Unlike `TriggerLink`'s
mana-cost factor, which only scales an already-resolved child cost with no base
of its own, energy gain has a genuine base: the trigger's authored
`energyPerSecond`. `increasedEnergyGain` is a direct multiplier (default `1`),
and this local resolve uses the same shared `StatFold` formula as mana.

Each source begins with empty energy. Every simulation update adds
`energyPerSecond * deltaTime`; whenever accrued energy reaches the next
threshold, the system emits a child event and consumes that fixed threshold.
Thresholds are floored positive, each update emits at most 256 children, and a
runtime rate at or below zero emits none.

Timed children use exactly the child set's compiled attributes: projectile
`count` and `spreadDegrees`, AOE `echoCount` and `scatterRadius`, and targeted
`echoCount`. `MultipleProjectilesSupport`, `MultipleAoesSupport`, and
`MultipleChainsSupport` apply before those values are compiled.

Directionality defaults:

- projectile child from any source: `SideSpray` — half the shots fan left of a
  wave heading and half fan right. Each shot's angle is randomized within
  `+/-child spreadDegrees/2` of that side's perpendicular line (i.e. the line 90
  degrees from the heading) — not around the heading itself. Interval projectile
  waves roll a fresh heading on every energy tick, whether the source is moving
  or stationary. All of this is seeded per spawner instance and per energy tick,
  so no two waves and no two spawners roll the same shots.
- AOE child from any source: spawned around the source center. Echo copies fan
  through `AOE spawn expansion systems`; each copy is placed in a deterministic
  random disk within the child AOE's `AoeDefinition.scatterRadius` around the center. With
  `AoeDefinition.scatterRadius = 0`, echo copies overlap at the center.

**HitEnergyTrigger**

Wires a projectile, AOE, or targeted source to an adjacent projectile or AOE
output. It authors positive `energyContributionMultiplier`, positive
`energyRequirementMultiplier`, and positive `retentionSeconds`. Compilation creates a
`RuntimeHitEnergyTrigger` composition containing the compiled `TriggeredSkill`
and a unique per-edge `AccumulatorId`; it does not wrap or subclass that skill.

```csharp
class HitEnergyTrigger : TriggerLink {
    float energyContributionMultiplier;
    float energyRequirementMultiplier;
    float retentionSeconds;
}
```

At fire time, projectile, AOE, lingering AOE, and targeted sources receive the
same plain `HitEnergyPayload`. Finalization deposits float energy into
target-local `TargetHitEnergy`; activation on a later update spends complete
requirements and retains fractional remainder. `HitEnergySpawn` contains only
output kind, faction, and registered template key, so the registered output
template remains authoritative. This target-local hit energy is separate from
the time-based interval energy used by `IntervalSpawnTrigger`.
Trigger links are tag-validated. An `IntervalSpawnTrigger` from a projectile
set to an AOE set is valid and compiles an AOE interval setup. A pulse AOE or
targeted interval source instead fails the generic source-tag check at Error
severity and compiles to no timed-child setup.

### Validation Warnings

Projectile authoring has `trackingEnabled` and `continuousCollision`. Tick `continuousCollision` for
fast straight projectiles that must not skip targets. A standard `0.1 x 0.15` projectile can
skip Bat-sized (`0.35`) targets after more than `0.8` units in two ticks. Existing MagicBolt
(speed 30) remains deliberately discrete. Tracking cannot combine with sweep.

Validation normally reports authored combinations that compile to no-ops without throwing
setup errors. Error-severity entries block the affected spawn. The current runtime exposes
results as
`SkillValidationWarning[]` from `SkillLoadoutValidator.Validate(...)`, and
`SkillDriver.ValidationWarnings` stores the latest compile warnings for
future UI.

Current warning cases:

- skill set slot missing a set or skill
- support tag does not match the skill tag
- trigger link is not between two valid skill sets
- trigger has no runtime-compatible tags
- trigger source or target tags do not match the neighboring skill sets
- targeted chain has a missing prefab, missing line-segment VFX, or non-positive link width
- targeted prefab fails `IsValidTemplate`; a baked `Hurtbox` is error severity
- targeted `chainCount`, `echoCount`, or `chainDelay` outside its range and will be clamped
- `chainDistance` is not positive: error, the skill will not spawn
- `chainCount > 1` with a non-positive `chainDamageFalloff`, so links after the first deal zero damage
- an `IntervalSpawnTrigger` targeted child costs more energy than its source can accrue over its
  lifetime, so it never spawns
- interval trigger source has no `Interval` tag (pulse AOE or targeted):
  `UnsupportedTriggerSource` Error from generic tag validation; it compiles to
  no timed-child setup
- `ContinuousCollisionCannotTrack`: error. `continuousCollision` cannot combine with tracking,
  including tracking enabled by a support; cast is refunded and does not fire.
- `TrackingProjectileMayTunnel`: advisory warning. A tracking projectile is fast enough to
  skip small targets; lower speed or remove tracking because sweep cannot be enabled.

---

## Legacy Skill Loadout Shape (Historical)

```csharp
class SkillLoadout {
    [SerializeReference] List<LoadoutSlot> slots;   // ordered; position encodes wiring
    int maxRootSets;
}
```

The slot list is the single authoring surface for both skill sets and trigger
wiring. Root sets (player-input-driven) are derived at compile time 鈥?any skill
set that does not appear as a trigger effect is a root. A skill set that appears
as an effect in one chain and a cause in another is compiled as triggered-only;
it is never fired directly by input.

---

## Legacy Compilation Description (Historical)

At equip time (`SkillDriver.Start`) the loadout compiles each root set
into a `RuntimeSkillDefinition` tree via `SkillSetCompiler`. Compilation
traverses the chain graph to build the full tree. It does not run per frame.

```
parseChains(slots) -> TriggerChain[]:
    for each SkillSetSlot at index i:
        if slots[i+1] is TriggerLinkSlot and slots[i+2] is SkillSetSlot:
            emit TriggerChain { cause=slots[i], link=slots[i+1], effect=slots[i+2] }

compile(SkillSet set, allChains, snapshot) -> RuntimeSkillDefinition:
    def = set.skill.Definition.DeepCopy()
    acc = new StatModifierAccumulator()
    SnapshotModifiers.Contribute(acc, snapshot)   // damage/area snapshot multipliers as Post terms
    for each support in set.supports:
        if support is IDamageModifiers.IBaseValueModifier:
            support.CollectAdded(new AddedSink(acc))
        if support is IPierceCountModifiers.IBaseValueModifier:
            support.CollectAdded(new AddedSink(acc))
        if support is IAreaSizeModifiers.IIncreasedModifier:
            support.CollectIncreases(new IncreasedSink(acc))
        if support is IAreaSizeModifiers.IMultiplierModifier:
            support.CollectMultipliers(new MultiplierSink(acc))
        if support is IProjectileSpeedModifiers.IMultiplierModifier:
            support.CollectMultipliers(new MultiplierSink(acc))
        if support is IDurationModifiers.IBaseValueModifier:
            support.CollectAdded(new AddedSink(acc))
        if support is IDurationModifiers.IIncreasedModifier:
            support.CollectIncreases(new IncreasedSink(acc))
        if support is IDurationModifiers.IMultiplierModifier:
            support.CollectMultipliers(new MultiplierSink(acc))
        if support is IRateModifiers.IIncreasedModifier:
            support.CollectIncreases(new IncreasedSink(acc))
        if support is IManaModifiers.IBaseValueModifier:
            support.CollectAdded(new AddedSink(acc))
        if support is IManaModifiers.IIncreasedModifier:
            support.CollectIncreases(new IncreasedSink(acc))
        if support is IManaModifiers.IMultiplierModifier:
            support.CollectMultipliers(new MultiplierSink(acc))
        // These are independent if checks; multi-kind supports visit every matching branch.
    for each support in set.supports:
        if def is ProjectileDefinition and support is IProjectileBehaviorModifier:
            support.ApplyToProjectile(new ProjectileBehaviorContext(def))
        else if def is AoeDefinitionBase and support is IAoeBehaviorModifier:
            support.ApplyToAoe(new AoeBehaviorContext(def))
    runtime = BuildRuntime(def, acc, snapshot)    // numeric fields resolve through the fold
    rate = acc.Resolve(Rate, set.skill.BaseRate)
    runtime.RecoveryTime = 1 / max(0.01, rate)
    for each chain in allChains where chain.cause == set:
        if chain.link is IntervalSpawnTrigger:
            compile chain.effect recursively
            switch compiled effect runtime type:
                RuntimeProjectileDefinition -> bake RuntimeChildSpawnSetup
                RuntimeAoeDefinition -> bake RuntimeAoeIntervalSpawnSetup
                RuntimeTargetedDefinition -> bake RuntimeTargetedIntervalSpawnSetup
        if chain.link is HitEnergyTrigger:
            triggered = compile chain.effect recursively
            create RuntimeHitEnergyTrigger composition using triggered,
                both link multipliers, retentionSeconds, and a unique AccumulatorId
            attach composition only to runtime for the adjacent source
    return runtime

compileLoadout(SkillLoadout loadout):
    snapshot = SkillStatAggregator.Aggregate(loadout)
    chains = parseChains(loadout.slots)
    effects = { chain.effect for each chain }
    rootSets = [ slot.skillSet for each SkillSetSlot in slots
                 if slot.skillSet not in effects ]
    for each rootSet:
        compiledSlots[i] = compile(rootSet, chains, snapshot)
    RegisterProjectileTypes()   // walk compiled trees; call combatRoot.RegisterTemplate per unique prefab
    RegisterAoeTypes()          // walk compiled trees; call combatRoot.RegisterType per unique AoeTypeDefinition
    RegisterSounds()            // register each root SpawnSound; store SkillSoundIds.SpawnId
    AssignHitEnergyAccumulatorIds() // mint one id per compiled edge
    RegisterSpawnTemplates()       // register outputs before payload construction
```

The compiled runtime tree feeds directly into the existing spawn request and
ECS simulation path. The ECS systems receive snapshots and are unaware of the
skill system above them.

### Type Registration

After compilation, `SkillDriver` recursively walks all compiled trees:
- Projectile prefabs: each unique `BasicAttackPrefab` is registered once with
  `CombatRoot.RegisterTemplate`; the returned `TypeId` is stored on the
  `RuntimeProjectileDefinition`.
- AOE definitions: each `RuntimeAoeDefinition` is converted to an
  `AoeTypeDefinition` and registered with `CombatRoot.RegisterType`; the returned
  `TypeId` is stored. `CombatRoot` deduplicates 鈥?re-registering the same reference
  returns the existing ID.

- Spawn sounds: every compiled definition's prefab-authored `SpawnSound` is
  registered recursively with `AudioRoot`; its stable id is stored in
  `SkillSoundIds.SpawnId` and copied into its spawn template. Root casts,
  interval children, on-hit spawns, and hit-energy activations emit at expansion.

- Hit-energy edges: each compiled `RuntimeHitEnergyTrigger` receives a dedicated
  `AccumulatorId`. Reused assets and identical values remain isolated by graph
  position. Its triggered skill is registered first; the resulting
  `HitEnergySpawn` kind/key reference becomes output authority.
- Energy-driven child spawn templates: each compiled `RuntimeChildSpawnSetup` or
  `RuntimeAoeIntervalSpawnSetup` builds one unified spawn template for its
  child domain and registers it with `CombatRoot.RegisterTimedSpawnTemplate`.
  The returned `TemplateKey` is copied onto the setup; ECS spawner components
  carry that key instead of embedding the full recursive child template.
- Each castable root also registers one spawn template for `SkillSpawnTranslator`.
  An interval child does not register an additional generic template: its
  interval template is the only entry used to materialize it.

`SkillDriver` registers a new compile's complete key set before releasing the
previous set. This keeps shared keys continuously owned across recompiles and
lets in-flight entities retain old keys through instance counting. It also
releases its set on root replacement and driver destruction.

### Spawn-Template Registry

Energy-driven child spawns store the spawn event itself as the template. The shared
`CombatScope` entity owns one registry per child domain:
`ProjectileSpawnTemplate` holds a `NativeHashMap<Hash128, ProjectileSpawnEvent>`
and `AoeSpawnTemplate` holds a `NativeHashMap<Hash128, AoeSpawnCommand>`. There is
no separate template-data shape and no template-to-event conversion step.

The runtime data is split into three tiers:
- Registry data: cold shared spawn events on the shared `CombatScope` entity,
  keyed by `Hash128 TemplateKey`. The stored events contain full child spawn
  behavior such as count/fan-out, damage, crit inputs, render data, and nested
  hit payload snapshots. Per-instance fields such as position, faction, source
  id, jitter seed, and deterministic tick index are left default in the stored
  event.
- Slim energy config: per-source `TimedSpawnComponent` keeps `Faction`,
  `SourceId`, `ChildKind`, `TemplateKey`, `EnergyPerSecond`, `EnergyThreshold`,
  and `JitterSeed`.
- Hot energy state: `TimedSpawnStateComponent` keeps accumulated energy and tick
  index. This state is reset when a timed-spawning projectile or lingering AOE
  is cold-created or reused from the pool.

One `TimedSpawnSystem` processes active timed-spawning projectile and lingering
AOE sources. When enough energy has accrued, it fetches the stored event by
`TemplateKey`, stamps the per-instance fields from the source entity, and
enqueues the existing `ProjectileSpawnEvent` or `AOE variant spawn event`. Children still
flow through the canonical event -> expansion -> command -> apply path.

`CombatRoot.RegisterTimedSpawnTemplate` hashes the stored event content and
inserts only if the key is absent. Identical child behavior shares one registry
entry; changing child template behavior such as `ProjectileDefinition.count`,
`AoeDefinition.echoCount`, or `AoeDefinition.scatterRadius` creates a different key, and selecting a previous behavior
reuses the previous key. Per-source energy rate, threshold, threshold jitter,
and jitter seed are not part of the template hash because they belong to the
individual energy config.

Registry entries are owner- and instance-counted. `SkillDriver` releases the
previous compile's registrations only after registering the new set; entries
still referenced by live or pooled entities survive until the late-simulation
sweep observes zero instances. Ad-hoc root spawns are pinned.

The timed-spawn loop clamps every tick advance to a positive minimum and caps
catch-up iterations per update. A bad or zero authored interval can produce only
a bounded number of child events in one update, so it cannot freeze the editor.

---

## Legacy Runtime Modification (Historical)

The compiled tree is a mutable runtime instance. SO templates are never
touched after compilation.

Mid-game changes re-compile the affected paths:

```
player adds a Support to SetA
鈫?recompile all root sets
鈫?re-register all projectile and AOE types

player inserts a TriggerLinkSlot + SkillSetSlot after SetA's slot
鈫?slot list updated
鈫?recompile all root sets   // SetA now has an outgoing chain

player swaps the SkillSet in a slot
鈫?recompile all root sets
```

Recompile cost is proportional to the depth of the outgoing link graph from
the changed set 鈥?typically two or three levels deep.

---

## Legacy Authoring Guide (Historical)

Player-facing types only - these are the only types that appear in loadout
editors, skill slot UIs, or player save data:

| Type | Role |
|---|---|
| `Skill` | Authored baseline for one spell or attack; carries base stat fields |
| `SkillSupport` | Base type for modifier supports |
| `StatModifierSupport` | Base type for augment supports with skill tag validation |
| `SkillSet` | One skill plus its supports |
| `SkillSetSlot` | Slot entry wrapping a `SkillSet` in the loadout list |
| `TriggerLinkSlot` | Slot entry wrapping a `TriggerLink` in the loadout list |
| `TriggerLink` | Trigger condition and parameters; no source/target references |
| `SkillLoadout` | Ordered slot list; live equipment state |

Internal runtime types (`CombatRoot`, ECS systems) are not exposed to the
player-facing authoring surface.

### Asset Locations

- `Assets/ScriptableObjects/Skills/`: Skill assets
- `Assets/ScriptableObjects/Skills/Supports/`: Support assets
- `Assets/ScriptableObjects/Skills/Sets/`: Skill Set assets

### Creating a Skill

1. `Assets > Create > PlayGround > Skills > Projectile Skill` or `AOE Skill`.
2. Assign sprite, material, and collision shape fields.
3. Set `baseRate` (attacks/casts per second) and positive `triggerEnergy`.
4. Set base behavior values (speed, damage, lifetime, etc.).

### Creating a Hit-Energy Activation

1. Create the output as a normal `Projectile Skill`, `AOE Skill`, or
   `Lingering AOE Skill`, and set its `triggerEnergy` base requirement.
2. Create a `SkillSet` for the output.
3. Create a separate projectile, AOE, or targeted source `SkillSet`, and set its
   `triggerEnergy` base contribution.
4. Wire source to output with `HitEnergyTrigger`, then set
   `energyContributionMultiplier`, `energyRequirementMultiplier`, and
   `retentionSeconds` on that link.

Example:

```text
[SkillSetSlot: SetA]
[TriggerLinkSlot: OnHit]
[SkillSetSlot: SetB_Applicator]
[TriggerLinkSlot: HitEnergyTrigger]
[SkillSetSlot: SetC_Output]
```

The applicator remains a plain `RuntimeProjectileDefinition` or
`RuntimeAoeDefinition`, so it still composes with `IntervalSpawn` and `OnHit`.
`RuntimeHitEnergyTrigger` is attached composition on that source.

**Contribution model.** If source `TriggerEnergy = 2` with contribution
multiplier `0.5`, each accepted hit deposits `1`. If output
`TriggerEnergy = 4` with requirement multiplier `1.5`, each activation costs
`6`. The `1e-3` floor applies to both effective values. Damage and all other
output behavior come only from the registered output template.

**Burst activation.** `HitEnergyActivationSystem` runs before current-update
finalization. It activates `floor(StoredEnergy / EnergyRequired)` times, capped
at 256 outputs per target/update, subtracts only emitted energy, and retains
fractional remainder plus overflow. Deposits finalized this update become
eligible on the next update.

**Composition.** To chain after an activated AOE, put a normal AOE trigger after
the output set, then wire the next source to its own hit-energy output:

```text
[SkillSetSlot: FirstApplicator]
[TriggerLinkSlot: HitEnergyTrigger]
[SkillSetSlot: FirstOutput]
[TriggerLinkSlot: OnHit]
[SkillSetSlot: SecondApplicator]
[TriggerLinkSlot: HitEnergyTrigger]
[SkillSetSlot: SecondOutput]
```
### Creating a Skill Set

1. `Assets > Create > PlayGround > Skills > Skill Set`.
2. Assign one Skill to the `skill` field.
3. Add Skill Supports to the `supports` array in desired order.

### Building the Slot List

The `SkillLoadout.slots` list uses `[SerializeReference]` 鈥?add entries via
the Unity inspector using the managed reference picker.

Each entry is a `SkillSetSlot` or a `TriggerLinkSlot`. Position determines
wiring: a `SkillSetSlot` at index `i` followed immediately by a
`TriggerLinkSlot` at `i+1` and a `SkillSetSlot` at `i+2` forms a trigger
chain. The left skill is always the cause; the right skill is always the effect.

**Simple skill (no trigger):**
```
[SkillSetSlot: SetA]
```

**Skill with one trigger:**
```
[SkillSetSlot: SetA] [TriggerLinkSlot: OnHit] [SkillSetSlot: SetB]
```

**Deep chain:**
```
[SkillSetSlot: SetA] [TriggerLinkSlot: ProjectileIntervalSpawn] [SkillSetSlot: SetB]
[SkillSetSlot: SetB] [TriggerLinkSlot: OnHit] [SkillSetSlot: SetC]
```
SetB appears as both effect (of SetA) and cause (for SetC). It is compiled as
a triggered-only set 鈥?not player-input-driven.

### Equipping on the Player

1. Create a `SkillLoadout` SO (`Assets > Create > PlayGround > Skills > Skill Loadout`).
2. Add `SkillSetSlot` and `TriggerLinkSlot` entries to `slots` in order.
3. Confirm `maxRootSets` covers the number of independent root skills.
4. Assign the `SkillLoadout` SO to `SkillDriver.loadout` on the player prefab.

---

## Examples

In all examples, sets have no knowledge of each other. The slot list is the
only place where wiring exists.

### Simple: single skill, no supports

```
Slots: [SetA: MagicBullet]

Root: SetA
```

Fires one magic bullet at base speed and damage.

---

### Augmented: modifier supports only

```
Slots: [SetA: [MultipleProjectiles(count=5, spread=40掳), Piercing(pierce=2)] + MagicBullet]

Root: SetA
```

Fires five piercing magic bullets spread across 40 degrees.

---

### Chained: one trigger link

```
Slots: [SetA: [MultipleProjectiles, Piercing] + MagicBullet]
       [OnHit]
       [SetB: ArcaneBurst]

Parsed chain: SetA 鈫?OnHit 鈫?SetB
Root: SetA
```

Each magic bullet explodes into an arcane burst on impact. SetB has no
supports 鈥?the burst fires at its own base radius and damage, independent of
SetA.

---

### Deep chain: two trigger links

```
Slots: [SetA: [MultipleProjectiles, Piercing] + MagicBullet]
       [IntervalSpawn(energyPerSecond=2)]
       [SetB: MagicBullet]
       [OnHit]
       [SetC: ArcaneBurst]

Parsed chains: SetA 鈫?ProjectileIntervalSpawn 鈫?SetB
               SetB 鈫?OnHit 鈫?SetC
Effects: {SetB, SetC}
Root: SetA
```

SetA fires many piercing bullets. Each projectile periodically spawns child
bullets (SetB) while in flight. Each child bullet releases an arcane burst
(SetC) on impact.

SetB appears at index 2 (effect of SetA) and index 3 (cause for SetC). It is
triggered only 鈥?never fired by player input.

SetB's bullet has no pierce 鈥?SetA's pierce support does not carry over.
SetC's burst radius is its own authored value, unaffected by SetA or SetB.

---

### Hit-energy chain

```
Slots: [SetA: MagicBullet]
       [OnHit]
       [SetB: VolatileApplicatorAoe]
       [HitEnergyTrigger]
       [SetC: VolatileOutputAoe]
```

SetA releases SetB through a normal on-hit link. SetB remains a plain AOE
source, while `HitEnergyTrigger` compiles SetC as edge composition attached to
SetB. When SetB hits a target, `CombatApplyFinalizeSingleSystem` deposits energy
under that edge's `AccumulatorId`. On the next simulation update,
`HitEnergyActivationSystem` spends complete requirements and emits SetC through
its registered spawn template.
---

## Set Isolation Rules

- Stat Modifier Supports only modify the Skill in the same set.
- Trigger Links carry no stats or behavior into the target set.
- The target set's Skill and Supports fully determine triggered behavior.
- Stat inheritance across sets does not exist.
- A set can appear as the effect in multiple chains (multiple causes pointing
  to it). It is compiled independently for each 鈥?edits propagate to all
  compiled instances on next recompile.

---

## Chain Depth and Trigger Scope

Triggers fire strictly by **slot position**, not by skill identity. A trigger chain only
connects the specific cause-effect pair defined by adjacent slots 鈥?no cross-chain
firing, no transitive propagation beyond the authored depth.

```
Skill1 鈫?Trigger 鈫?Skill2
```

Only Skill1 fires Skill2 via the trigger. Skill2 never re-fires itself through that
trigger, even if it hits the same targets under the same conditions.

```
Skill1 鈫?TriggerA 鈫?Skill2 鈫?TriggerB 鈫?Skill3
```

Skill1 fires Skill2 via TriggerA. Skill2 fires Skill3 via TriggerB. No other
trigger relationships exist. Skill3 has no outgoing trigger, regardless of what
SkillSets it shares identity with.

### Self-Referential Chains

A SkillSet asset may appear as both cause and effect in the same chain:

```
[SkillSetSlot: SetA] [TriggerLinkSlot: OnHit] [SkillSetSlot: SetA]
```

This is valid. SOs are configuration templates, not instances. Each slot is always an
**independent compilation unit** 鈥?two slots that reference the same asset still produce
two separate `RuntimeSkillDefinition` instances, just as two slots with different assets
would. Instance identity is determined by slot position, not asset identity.

The cause slot compiles normally with its outgoing trigger wired. The effect slot
compiles with an empty chain list. This is a **recursion guard** in the compiler: when
the compiler detects it would recurse into the same SkillSet object it is already
compiling (cause and effect share the same SO reference), it passes an empty chain list
for that pass to prevent an infinite loop. The effect instance produced is still fully
independent 鈥?it just carries no outgoing trigger setup.

Practical use: one source can spawn another compiled instance of the same set
through a normal trigger. If each instance has a `HitEnergyTrigger` edge, every
edge receives its own `AccumulatorId`, so target-local accumulation stays
independent even when assets and effective values are identical.

# Skill System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Concepts

**Skill** — an active spell or attack. Defines what is spawned: a projectile,
an AOE, a beam. Owns base visual, collision shape, base rate (attacks/casts
per second), and behavior data. A Skill slotted alone fires with base behavior and no
augmentation.

**Stacking Support** - a conversion support placed on a normal skill set to make
that set a stack detonation. It owns threshold, lifetime, stacks-per-hit, and
presentation config. A stacking set is triggered-only and fires only when reached
through a `StackTrigger`.

**Stat Modifier Support** - augments the Skill in the same set through typed
modifier kinds: flat base additions, increased percentages, Pre/Post
multipliers, or behavior contexts. Only ever affects the one Skill it shares a
set with. No cross-set influence.

**Skill Set** — the unit of authoring. Contains one or more Skills and zero or
more Skill Supports. Self-contained: its Skills are only modified by its own
Supports. Skill Sets have no knowledge of what triggers them or what they
trigger, except that a conversion support can mark the set triggered-only.

**Trigger Link** — external wiring between two Skill Sets. Owned by the
loadout, not by either set. Defines the source set whose events fire the
trigger, the target set that fires when triggered, the trigger condition, and
any trigger-specific parameters.

**Player Loadout** — owns root Skill Sets (those fired by player input), all
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
┌─────────────────────────────────┐
│  Layer 1: Equipment state       │
│  Skill, SkillSupport,          │
│  SkillSet, TriggerLink,         │
│  PlayerLoadout                  │
│                                 │
│  Mutable. Live equipment state. │
│  Uses Layer 1.5 to produce      │
│  PlayerStatSnapshot on change.  │
└────────────────┬────────────────┘
                 │ PlayerStatSnapshot + SkillSet
┌────────────────▼────────────────┐
│  Layer 2 (orchestration layer)  │
│  PlayerSkillDriver              │
│  SkillSlotState[] (cooldowns)   │
│  RuntimeSkillDefinition[]       │
│                                 │
│  Owns runtime slot state.       │
│  Gates input → internals.       │
│  Uses Layer 2.5 to dispatch.    │
└────────────────┬────────────────┘
                 │ drives
┌────────────────▼────────────────┐
│  Combat runtime (internal)      │
│  CombatRoot, ECS systems,       │
│  MonoBehaviours                 │
└─────────────────────────────────┘

Layer 1.5 (PlayerStatAggregator) — stateless utility used by Layer 1. Not a chain tier.
Layer 2.5 (SkillSpawnTranslator) — stateless utility used by Layer 2. Not a chain tier.
```

### Layer 1: Equipment State

Types: `Skill`, `StatModifierSupport`, `ConversionSupport`, `SkillSet`,
`TriggerLink`, `PlayerLoadout`

- Owns all stat sources: base skill stats, base rate, supports, items,
  buffs, character level
- `PlayerLoadout` is live mutable equipment state — not just an authoring template
- `Skill` SOs are immutable authored templates; `PlayerLoadout` holds mutable slot references into them
- Save/load serializes slot references, not compiled runtime trees
- Emits change events when equipment or stat sources change (consumed by Layer 1.5)
- No per-frame stat math; scaling is rebaked on equipment swaps, buff changes, level up, etc.

### Layer 1.5: Stat Resolution

Types: `PlayerStatSnapshot`, `PlayerStatAggregator`

Stateless utility - not a chain tier. Pure aggregation function: inputs in, flat snapshot
out, no stored state. Collapses player-level Layer 1 stat sources into a flat
snapshot. The compiler feeds snapshot terms into the same per-stat fold used by
supports: Post multipliers where applicable, and increased percentages where the
stat is authored as increased scaling. Owns no data - all sources come from
Layer 1.

`PlayerStatSnapshot` fields:
- `increasedRatePercent`
- `damageMultiplier`
- `areaSizeMultiplier`

Baking:
- `increasedRatePercent` contributes an increased percent to the `Rate` fold
- `damageMultiplier` contributes a Post multiplier to the `Damage` fold
- `areaSizeMultiplier` contributes a Post multiplier to the `AreaSize` fold
- `recoveryTime` is derived after folding: `recoveryTime = 1 / rate`

### Layer 2: Orchestration

Types: `PlayerSkillDriver`, `SkillSlotState`, `SkillSetCompiler`

- Reads the flat `PlayerStatSnapshot` from Layer 1.5 — does not compute stats
- Compiles `SkillSet` + snapshot into `RuntimeSkillDefinition` trees whenever stats change
- After compile: registers `BasicAttackPrefab` templates and `AoeTypeDefinition`
  entries with the bound `CombatRoot`; stores resolved type IDs into compiled
  definitions. Re-registration on equip change is safe — `CombatRoot` deduplicates by reference.
- Registers interval child spawn templates with the bound `CombatRoot`; compiled
  interval setup stores only the returned content-hash `TemplateKey` plus timer
  configuration.
- Owns `SkillSlotState` per root slot: tracks cooldown elapsed time, gates input-driven casts
- On player input: checks slot cooldown; if ready, calls `SkillSpawnTranslator` and resets timer
- On Layer 1 change: recompiles affected paths, re-registers types, updates slot `recoveryTime`
- Holds scene-side references to `CombatRoot`, `AudioManager` — internal wiring only

`SkillSlotState` per root slot:
- `elapsedSinceLastFire` — ticked each frame, reset on successful fire
- `recoveryTime` — copied from `RuntimeSkillDefinition` whenever stats change;
  derived internally as `1 / rate`, not authored directly
- `IsReady` — `elapsedSinceLastFire >= recoveryTime`
- `CooldownProgress` — 0..1, for UI

### Layer 2.5: Translation

Types: `SkillSpawnTranslator`

Stateless utility — not a chain tier. `CombatRoot` requires visual types to be
pre-registered before any spawn; it bakes sprite mesh, material, and VFX
handlers at registration time and returns a type ID used in spawn commands.
IDs are generated inside `CombatRoot`; it deduplicates (re-registering same
definition returns the existing ID). Type ID resolution is state — it belongs
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
self-contained — it does not reference other sets and carries no trigger data.

---

## Skills

Skills are ScriptableObjects that carry the authored baseline for one type of
attack. The baseline is a definition tree of plain serializable C# classes
containing visual, collision, and behavior data. Skills do not own augmentation
— that belongs to Supports.

`Skill` is abstract. Concrete types are `ProjectileSkill`, regular `AoeSkill`,
and `LingeringAoeSkill`, each holding their typed definition inline. Regular
and lingering AOE skills derive from the same AOE skill base. The SO is never
mutated at runtime.

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

```

Skill tags are runtime-authoring metadata used for validation, not hard gates.
Current tags:

| Tag | Meaning |
|---|---|
| `Projectile` | Skill compiles to a projectile runtime definition |
| `Aoe` | Skill compiles to an AOE runtime definition |

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

### ProjectileDefinition

```
ProjectileDefinition
 ├─ prefab:    BasicAttackPrefab   ← sprite, material, hitbox collider, particle effects
 └─ behavior:  speed, lifetime, damage, count, spreadDegrees, jitterDegrees,
               pierceCount, repeatHitCooldown,
               trackingEnabled, trackingRange, trackingTurnSpeed,
               trackingQueryInterval, trackingInitialDelay,
               directDamageEnabled
```

The prefab is a visual and collision preset. The runtime bakes collision shape
and render data from it at load time, identical to current baking behavior.
Behavior fields on the definition drive simulation — the prefab contributes
nothing to behavior.

### AoeDefinition

```
AoeDefinition
 ├─ prefab:    BasicAoePrefab   ← sprite, material, hitbox collider, particle effects
 └─ behavior:  baseAreaSize, damage, echoCount, scatterRadius,
               directDamageEnabled
```

Regular AOE content compiles as pulse AOE. It does not expose lifetime or tick
interval, and the runtime receives `0` for both timing fields.

### LingeringAoeDefinition

```
LingeringAoeDefinition
 ├─ prefab:    LingeringAoePrefab ← sprite, material, hitbox collider, particle effects
 └─ behavior:  baseAreaSize, damage, lifetimeSeconds, tickIntervalSeconds,
               echoCount, scatterRadius, directDamageEnabled
```

### StackingSupport

```
StackingSupport
  stack rules: stackThreshold, debuffLifetimeSeconds, stacksPerHit,
               debuffName, cosmeticDebuffStatus
```

A stacking detonation is authored as a normal skill set whose skill is the
detonation effect and whose supports include `StackingSupport`. The support
converts that set to `RuntimeStackingDetonation`, marks it triggered-only, and
keeps the set out of player-cast roots. The applicator is not bundled into this
set; it is any normal projectile or AOE set wired to the stacking set by a
`StackTrigger` link.

On each applicator hit, ECS writes one target-bucketed hit payload containing
the registration-derived debuff key, the stack contribution, threshold,
lifetime refresh, and detonation snapshot. `HitApplyFinalizeSystem` owns stack
accrual into each target proxy's `TargetStackEntry` buffer. `StatusProcessSystem`
then owns tick, fizzle, and threshold detonation processing.

Detonation supports AOE and projectile outputs. A projectile detonation is a
nova from the target point and reuses the existing AOE projectile-burst spawn
path: the compiled detonation carries an `AoeProjectileBurstSnapshot`, and
`StatusProcessSystem` emits a `ProjectileSpawnEvent` for
`ProjectileSpawnExpansionSystem`. `SummedProjectileCount` becomes the nova
count. `SummedDamage` is treated as total nova damage and is split across the
spawned projectiles.

The debuff key is minted during runtime registration for each compiled
`RuntimeStackingDetonation`. It is not authored and is not the detonation type
id. Two slots using the same stacking set still compile to separate runtime
instances with separate keys, so their stacks cannot collide. `debuffName` and
`cosmeticDebuffStatus` are presentation flavor only.

---

## Supports

Each support derives from `SkillSupport`. Augment supports derive from
`StatModifierSupport`, which carries `SupportedSkillTags` for validation, and
then implement one or more kind interfaces. `ConversionSupport` stays separate:
it can replace the compiled runtime shape after normal stat and behavior baking.
`StackingSupport` is the current conversion support and marks its set
triggered-only.

The four augment kinds are composable interfaces, not mutually exclusive base
classes:

```csharp
abstract class SkillSupport : ScriptableObject { }

abstract class StatModifierSupport : SkillSupport {
    public abstract SkillDefinitionTags SupportedSkillTags { get; }
}

interface IBaseValueModifier {
    void CollectAdded(AddedSink sink);              // flat added value
}

interface IIncreasedModifier {
    void CollectIncreases(IncreasedSink sink);      // summed increased percent
}

interface IMultiplierModifier {
    void CollectMultipliers(MultiplierSink sink);   // Pre/Post multiplier
}

interface IProjectileBehaviorModifier {
    void ApplyToProjectile(ProjectileBehaviorContext ctx);
}

interface IAoeBehaviorModifier {
    void ApplyToAoe(AoeBehaviorContext ctx);
}

abstract class ConversionSupport : SkillSupport {
    public abstract bool ConvertsToTriggeredOnly { get; }
    public abstract RuntimeSkillDefinition Compile(
        SkillDefinition definition,
        RuntimeSkillDefinition runtime,
        PlayerStatSnapshot snapshot);
}
```

A support may implement more than one kind. `PiercingSupport` is both an
`IBaseValueModifier` for `PierceCount` and an `IProjectileBehaviorModifier` for
`RepeatHitCooldown`. Each interface receives only its constrained sink or
behavior context, so an increased modifier cannot write base values or multiply
stats.

Numeric stats use one fold per stat:

```text
value = (base * preMultiplier + added) * (1 + increased) * postMultiplier
```

Increases are summed, multipliers are multiplied, and support order no longer
changes numeric output. Player snapshot terms feed this same fold:
`DamageMultiplier` contributes a Post multiplier to `Damage`,
`AreaSizeMultiplier` contributes a Post multiplier to `AreaSize`, and
`increasedRatePercent` contributes to the summed increased term on `Rate`.

Rate uses the same fold as other stats. `IncreasedRateSupport` and the player
`increasedRatePercent` both feed the increased bucket, then cooldown is derived
from the folded rate:

```text
rate = Resolve(Rate, baseRate)
recoveryTime = 1 / max(0.01, rate)
```

Current augment supports:

| Support | Kind interface(s) | Stat / behavior contribution |
|---|---|---|
| Multiple Projectiles | `IProjectileBehaviorModifier` | Sets projectile `count`, `spreadDegrees` |
| Multiple AOEs | `IAoeBehaviorModifier` | Sets AOE `echoCount`, `scatterRadius` |
| Piercing | `IBaseValueModifier`, `IProjectileBehaviorModifier` | Adds `PierceCount`; sets projectile `repeatHitCooldown` |
| Homing | `IProjectileBehaviorModifier` | Enables tracking and sets turn speed/query interval |
| Concentrated Effect | `IMultiplierModifier` | Post multiplier on `AreaSize` only |
| Increased AOE Effect | `IIncreasedModifier` | Increased percent on `AreaSize` |
| Faster Projectiles | `IMultiplierModifier` | Post multipliers on `ProjectileSpeed`, `ProjectileLifetime` |
| Added Damage | `IBaseValueModifier` | Adds `Damage` |
| Increased Skill Speed | `IIncreasedModifier` | Increased percent on `Rate` |

Supports also declare compatible skill tags:

| Support | Compatible tags |
|---|---|
| Multiple Projectiles | `Projectile` |
| Multiple AOEs | `Aoe` |
| Piercing | `Projectile` |
| Homing | `Projectile` |
| Faster Projectiles | `Projectile` |
| Added Damage | `Projectile`, `Aoe` |
| Concentrated Effect | `Aoe` |
| Increased AOE Effect | `Aoe` |
| Increased Skill Speed | `Projectile`, `Aoe` |

Example: putting Multiple Projectiles on an AOE skill is allowed, but it does
nothing and validation returns a warning.

---

## Loadout Slots

The `PlayerLoadout` is a flat ordered list of `LoadoutSlot` entries. Each slot
is either a `SkillSetSlot` or a `TriggerLinkSlot`. Position determines
relationship — no explicit source/target references exist.

```csharp
abstract class LoadoutSlot { }

class SkillSetSlot : LoadoutSlot {
    SkillSet skillSet;
}

class TriggerLinkSlot : LoadoutSlot {
    [SerializeReference] TriggerLink link;
}
```

`PlayerLoadout.slots` uses `[SerializeReference]` so polymorphic `TriggerLink`
instances inside `TriggerLinkSlot` serialize correctly in Unity.

### Parsing Rule

At compile time, `PlayerSkillDriver` scans the slot list. At every
`SkillSetSlot` at index `i`, if `slots[i+1]` is a `TriggerLinkSlot` and
`slots[i+2]` is a `SkillSetSlot`, those three form a `TriggerChain`:

```
cause  = slots[i].skillSet       ← left: always the trigger source
link   = slots[i+1].link         ← trigger type and parameters
effect = slots[i+2].skillSet     ← right: always the trigger target
```

`TriggerChain` is a runtime-only struct — it is never serialized.

### Root Detection

A `SkillSetSlot` is a **root** (fired by player input) if its skill set does not
appear as an `effect` in any parsed chain. All other skill sets are triggered.

```
slots: [SetA | ProjectileIntervalSpawn | SetB | OnImpactAoe | SetC]

chains:  SetA → ProjectileIntervalSpawn → SetB
         SetB → OnImpactAoe → SetC

effects: {SetB, SetC}
roots:   {SetA}         ← SetA is the only player-input slot
```

### Trigger Types

`TriggerLink` is abstract with no fields. The cause/effect relationship is
implicit from slot position, not stored on the link.

```csharp
abstract class TriggerLink { }
```

**ProjectileIntervalSpawnTrigger**

The cause duration skill periodically spawns child projectiles from the effect
set. Sources may be projectiles or lingering AOEs. Pulse AOEs have no lifetime
to tick on, so they validate with a warning and compile to no interval setup.
Effect must compile to a `RuntimeProjectileDefinition`.

```csharp
class ProjectileIntervalSpawnTrigger : TriggerLink {
    float intervalSeconds;
    float intervalJitterPercent;
    int spawnCount;
    float sideSpreadDegrees;
}
```

Compatible tags: source `Projectile` or `Aoe`, target `Projectile`.
Projectile sources compile a `RuntimeChildSpawnSetup` onto
`RuntimeProjectileDefinition.ChildSpawnSetup`. Lingering AOE sources compile the
same setup onto `RuntimeAoeDefinition.ChildSpawnSetup`.

**AoeIntervalSpawnTrigger**

The cause duration skill periodically spawns child AOEs from the effect set.
Sources may be projectiles or lingering AOEs. Pulse AOEs have no lifetime to
tick on, so they validate with a warning and compile to no interval setup.
Effect must compile to a `RuntimeAoeDefinition`.

```csharp
class AoeIntervalSpawnTrigger : TriggerLink {
    float intervalSeconds;
    float intervalJitterPercent;
    int spawnCount;
    float sideSpreadDegrees;
}
```

Compatible tags: source `Projectile` or `Aoe`, target `Aoe`.
Projectile sources compile a `RuntimeAoeIntervalSpawnSetup` onto
`RuntimeProjectileDefinition.AoeIntervalSpawnSetup`. Lingering AOE sources
compile the same setup onto `RuntimeAoeDefinition.AoeIntervalSpawnSetup`.

Interval source/child support:

| Source / Child | Projectile child | AOE child |
|---|---|---|
| Projectile source | `ProjectileIntervalSpawnTrigger` | `AoeIntervalSpawnTrigger` |
| Lingering AOE source | `ProjectileIntervalSpawnTrigger` | `AoeIntervalSpawnTrigger` |
| Pulse AOE source | warning, no-op | warning, no-op |

`intervalJitterPercent` is clamped from `0` to `100` and converted at compile
time to jitter seconds using `intervalSeconds * intervalJitterPercent / 100`.
The compiled jitter seconds are passed into the ECS interval spawner and applied
when scheduling interval ticks.

`spawnCount` is **additive** with the effect set's own multiplicity. For
projectile children this means `childDefinition.Count + spawnCount`, floored to
`1`. For AOE children this means `childDefinition.EchoCount + spawnCount`,
floored to `1`; each echoed copy then follows the child AOE's
`scatterRadius`.

Directionality defaults:

- projectile child from projectile source: velocity-relative `SideSpray` across
  `sideSpreadDegrees`
- projectile child from AOE source: radial 360-degree fan from the AOE center;
  `sideSpreadDegrees` is ignored
- AOE child from any source: spawned around the source center. Echo copies fan
  through `AoeSpawnExpansionSystem`; each copy is placed in a deterministic
  random disk within the child `scatterRadius` around the center. With
  `scatterRadius = 0`, echo copies overlap at the center.

**OnImpactAoeTrigger**

Fires the effect set as an AOE when the cause skill hits. The cause may be a
projectile or an AOE; the effect must compile to a `RuntimeAoeDefinition`. For a
projectile source the AOE is centered at the impact point and compiles into
`RuntimeProjectileDefinition.ImpactAoeDefinition`. For an AOE source it fires on
the AOE's hit and compiles into `RuntimeAoeDefinition.OnHitAoeSpawnDefinition`
(the same field as `OnAoeHitSpawnTrigger`).

```csharp
class OnImpactAoeTrigger : TriggerLink { }
```

Compatible tags: source `Projectile` or `Aoe`, target `Aoe`.

**OnImpactProjectileTrigger**

Fires the effect set as a burst of projectiles when the cause skill hits. The
cause may be a projectile or an AOE; the effect must compile to a
`RuntimeProjectileDefinition`. For a projectile source the burst originates at
the impact point aimed back from impact, and compiles into
`RuntimeProjectileDefinition.ImpactProjectileDefinition`. For an AOE source the
burst fires on each AOE hit and compiles into
`RuntimeAoeDefinition.OnHitProjectileSpawnDefinition`, materialized as the
`AoeProjectileBurstSnapshot` on the AOE's `AoeHitSpawnComponent`.

```csharp
class OnImpactProjectileTrigger : TriggerLink {
    int spawnCount;
    float spreadDegrees;
}
```

Compatible tags: source `Projectile` or `Aoe`, target `Projectile`.
`spawnCount` is **additive** with the effect set's own projectile count: the
impact burst size is `effectDefinition.Count + spawnCount`, floored to `1`. A
`spawnCount` of `0` means the effect set's own count alone determines the burst.
`spreadDegrees` overrides the effect set's spread and fans the burst around the
back-aimed impact direction. Proj→proj→proj nesting is not supported (a value-type
struct cannot be recursive); a nested impact-projectile chain on the effect is
dropped with a compile warning. From an AOE source the burst is a flat
`AoeProjectileBurstSnapshot`, so the spawned projectile's own impact AOE/projectile
chains cannot fire and are dropped with a compile warning. Only top-level and
interval-spawned AOEs carry the on-hit burst; an AOE reached via a projectile's
impact-AOE or another AOE's on-hit spawn cannot (those snapshots have no burst
slot).

**OnAoeHitSpawnTrigger**

Fires the effect set as an AOE when the source AOE hits a target.

```csharp
class OnAoeHitSpawnTrigger : TriggerLink { }
```

Compatible tags: source `Aoe`, target `Aoe`.

**StackTrigger**

Wires a normal applicator set to a stacking detonation set. The source is a
plain projectile or AOE runtime definition. The effect must be a normal skill
set with `StackingSupport`, which compiles to `RuntimeStackingDetonation`.
At compile time the trigger stores the detonation on the applicator; at spawn
time the applicator bakes a `StackEffectSnapshot` into its hit payload.

```csharp
class StackTrigger : TriggerLink { }
```

Compatible tags: source `Projectile` or `Aoe`, target set must have
`StackingSupport`.
Trigger links are also tag-validated but not blocked. A
`ProjectileIntervalSpawnTrigger` from a projectile set to an AOE set is allowed
in the loadout, but no interval setup is compiled and validation returns a
warning.

### Validation Warnings

Validation is non-blocking. It reports authored combinations that compile to
no-ops without throwing setup errors. The current runtime exposes warnings as
`SkillValidationWarning[]` from `SkillLoadoutValidator.Validate(...)`, and
`PlayerSkillDriver.ValidationWarnings` stores the latest compile warnings for
future UI.

Current warning cases:

- skill set slot missing a set or skill
- support tag does not match the skill tag
- trigger link is not between two valid skill sets
- trigger has no runtime-compatible tags
- trigger source or target tags do not match the neighboring skill sets
- interval trigger source is a pulse AOE instead of a projectile or lingering
  AOE
- stacking set is not the effect of a `StackTrigger`
- `StackTrigger` targets a set without `StackingSupport`
- stacking set is targeted by a normal trigger link

---

## Player Loadout

```csharp
class PlayerLoadout {
    [SerializeReference] List<LoadoutSlot> slots;   // ordered; position encodes wiring
    int maxRootSets;
}
```

The slot list is the single authoring surface for both skill sets and trigger
wiring. Root sets (player-input-driven) are derived at compile time — any skill
set that does not appear as a trigger effect is a root. A skill set that appears
as an effect in one chain and a cause in another is compiled as triggered-only;
it is never fired directly by input.

---

## Compilation

At equip time (`PlayerSkillDriver.Start`) the loadout compiles each root set
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
        if support is IBaseValueModifier:
            support.CollectAdded(new AddedSink(acc))
        if support is IIncreasedModifier:
            support.CollectIncreases(new IncreasedSink(acc))
        if support is IMultiplierModifier:
            support.CollectMultipliers(new MultiplierSink(acc))
        // These are independent if checks; multi-kind supports visit every matching branch.
    for each support in set.supports:
        if def is ProjectileDefinition and support is IProjectileBehaviorModifier:
            support.ApplyToProjectile(new ProjectileBehaviorContext(def))
        else if def is AoeDefinitionBase and support is IAoeBehaviorModifier:
            support.ApplyToAoe(new AoeBehaviorContext(def))
    runtime = BuildRuntime(def, acc, snapshot)    // numeric fields resolve through the fold
    for each support in set.supports:
        if support is ConversionSupport:
            runtime = support.Compile(set.skill.Definition, runtime, snapshot)
    rate = acc.Resolve(Rate, set.skill.BaseRate)
    runtime.RecoveryTime = 1 / max(0.01, rate)
    for each chain in allChains where chain.cause == set:
        if chain.link is ProjectileIntervalSpawnTrigger:
            compile chain.effect recursively -> RuntimeProjectileDefinition
            bake RuntimeChildSpawnSetup onto runtime.ChildSpawnSetup
        if chain.link is AoeIntervalSpawnTrigger:
            compile chain.effect recursively -> RuntimeAoeDefinition
            bake RuntimeAoeIntervalSpawnSetup onto runtime.AoeIntervalSpawnSetup
        if chain.link is OnImpactAoeTrigger:
            compile chain.effect recursively -> RuntimeAoeDefinition
            if runtime is projectile: set runtime.ImpactAoeDefinition
            else if runtime is AOE:   set runtime.OnHitAoeSpawnDefinition
        if chain.link is OnImpactProjectileTrigger:
            compile chain.effect recursively -> RuntimeProjectileDefinition
            if runtime is projectile: set runtime.ImpactProjectileDefinition
            else if runtime is AOE:   set runtime.OnHitProjectileSpawnDefinition
        if chain.link is OnAoeHitSpawnTrigger:
            compile chain.effect recursively to RuntimeAoeDefinition
            set runtime.OnHitAoeSpawnDefinition
        if chain.link is StackTrigger:
            compile chain.effect recursively to RuntimeStackingDetonation
            set runtime.StackingDetonation
    return runtime

compileLoadout(PlayerLoadout loadout):
    snapshot = PlayerStatAggregator.Aggregate(loadout)
    chains = parseChains(loadout.slots)
    effects = { chain.effect for each chain }
    rootSets = [ slot.skillSet for each SkillSetSlot in slots
                 if slot.skillSet not in effects
                 and no support ConvertsToTriggeredOnly ]
    for each rootSet:
        compiledSlots[i] = compile(rootSet, chains, snapshot)
    RegisterProjectileTypes()   // walk compiled trees; call combatRoot.RegisterTemplate per unique prefab
    RegisterAoeTypes()          // walk compiled trees; call combatRoot.RegisterType per unique AoeTypeDefinition
    AssignStackingDebuffKeys()  // mint one dedicated key per compiled RuntimeStackingDetonation
    RegisterIntervalTemplates() // content-hash child spawn templates; store TemplateKey on interval setup
```

The compiled runtime tree feeds directly into the existing spawn request and
ECS simulation path. The ECS systems receive snapshots and are unaware of the
skill system above them.

### Type Registration

After compilation, `PlayerSkillDriver` recursively walks all compiled trees:
- Projectile prefabs: each unique `BasicAttackPrefab` is registered once with
  `CombatRoot.RegisterTemplate`; the returned `TypeId` is stored on the
  `RuntimeProjectileDefinition`.
- AOE definitions: each `RuntimeAoeDefinition` is converted to an
  `AoeTypeDefinition` and registered with `CombatRoot.RegisterType`; the returned
  `TypeId` is stored. `CombatRoot` deduplicates — re-registering the same reference
  returns the existing ID.

- Stacking detonations: each compiled `RuntimeStackingDetonation`
  receives a dedicated debuff key during the same registration walk if it does
  not already have one. The key is per compiled instance and separate from AOE
  type registration.
- Interval child spawn templates: each compiled `RuntimeChildSpawnSetup` or
  `RuntimeAoeIntervalSpawnSetup` builds one unified spawn template for its
  child domain and registers it with `CombatRoot.RegisterTimedSpawnTemplate`.
  The returned `TemplateKey` is copied onto the setup; ECS spawner components
  carry that key instead of embedding the full recursive child template.

Registration re-runs via `BindAoeRoot` whenever `CombatRoot` is wired after compile.

### Spawn-Template Registry

Interval spawns store the spawn event itself as the template. The shared
`CombatScope` entity owns one registry per child domain:
`ProjectileSpawnTemplate` holds a `NativeHashMap<Hash128, ProjectileSpawnEvent>`
and `AoeSpawnTemplate` holds a `NativeHashMap<Hash128, AoeSpawnEvent>`. There is
no separate template-data shape and no template-to-event conversion step.

The runtime data is split into three tiers:
- Registry data: cold shared spawn events on the shared `CombatScope` entity,
  keyed by `Hash128 TemplateKey`. The stored events contain full child spawn
  behavior such as count/fan-out, damage, crit inputs, render data, and nested
  hit payload snapshots. Per-instance fields such as position, faction, source
  id, jitter seed, and deterministic tick index are left default in the stored
  event.
- Slim timer config: per-source `TimedSpawnComponent` keeps `Faction`,
  `SourceId`, `ChildKind`, `TemplateKey`, `IntervalSeconds`,
  `IntervalJitterSeconds`, and `JitterSeed`.
- Hot timer state: `TimedSpawnStateComponent` keeps mutable cooldown and tick
  index. This state is reset when a timed-spawning projectile or lingering AOE
  is cold-created or reused from the pool.

One `TimedSpawnSystem` processes active timed-spawning projectile and lingering
AOE sources. When a cooldown is due, it fetches the stored event by
`TemplateKey`, stamps the per-instance fields from the source entity, and
enqueues the existing `ProjectileSpawnEvent` or `AoeSpawnEvent`. Children still
flow through the canonical event -> expansion -> command -> apply path.

`CombatRoot.RegisterTimedSpawnTemplate` hashes the stored event content and
inserts only if the key is absent. Identical child behavior shares one registry
entry; changing behavior such as `spawnCount` creates a different key, and
selecting a previous behavior reuses the previous key. Jitter seed is not part
of the template hash because it belongs to the individual timer config.

Registry entries are never recycled in the current implementation. This keeps
old pooled or still-active spawners valid after loadout recompiles. A future
enhancement may add reference counts plus a grace-period sweep, but v1 relies on
the small number of distinct compiled behaviors per session.

The timed-spawn loop clamps every tick advance to a positive minimum and caps
catch-up iterations per update. A bad or zero authored interval can produce only
a bounded number of child events in one update, so it cannot freeze the editor.

---

## Runtime Modification

The compiled tree is a mutable runtime instance. SO templates are never
touched after compilation.

Mid-game changes re-compile the affected paths:

```
player adds a Support to SetA
→ recompile all root sets
→ re-register all projectile and AOE types

player inserts a TriggerLinkSlot + SkillSetSlot after SetA's slot
→ slot list updated
→ recompile all root sets   // SetA now has an outgoing chain

player swaps the SkillSet in a slot
→ recompile all root sets
```

Recompile cost is proportional to the depth of the outgoing link graph from
the changed set — typically two or three levels deep.

---

## Authoring

Player-facing types only - these are the only types that appear in loadout
editors, skill slot UIs, or player save data:

| Type | Role |
|---|---|
| `Skill` | Authored baseline for one spell or attack; carries base stat fields |
| `SkillSupport` | Base type for modifier and conversion supports |
| `StatModifierSupport` | Base type for augment supports with skill tag validation |
| `StackingSupport` | Converts a normal skill set into a stack detonation |
| `SkillSet` | One skill plus its supports |
| `SkillSetSlot` | Slot entry wrapping a `SkillSet` in the loadout list |
| `TriggerLinkSlot` | Slot entry wrapping a `TriggerLink` in the loadout list |
| `TriggerLink` | Trigger condition and parameters; no source/target references |
| `PlayerLoadout` | Ordered slot list; live equipment state |

Internal runtime types (`CombatRoot`, ECS systems) are not exposed to the
player-facing authoring surface.

### Asset Locations

- `Assets/ScriptableObjects/Skills/`: Skill assets
- `Assets/ScriptableObjects/Skills/Supports/`: Support assets
- `Assets/ScriptableObjects/Skills/Sets/`: Skill Set assets

### Creating a Skill

1. `Assets > Create > PlayGround > Skills > Projectile Skill` or `AOE Skill`.
2. Assign sprite, material, and collision shape fields.
3. Set `baseRate` (attacks/casts per second).
4. Set base behavior values (speed, damage, lifetime, etc.).

### Creating a Stacking Detonation

A stacking detonation is a normal skill set plus `StackingSupport`. The skill in
that set is the detonation effect. The support owns stack config and makes the
set triggered-only.

1. Create the detonation skill as a normal `Projectile Skill`, `AOE Skill`, or
   `Lingering AOE Skill`.
2. Create a `StackingSupport` and set `stackThreshold`,
   `debuffLifetimeSeconds`, `stacksPerHit`, `debuffName`, and
   `cosmeticDebuffStatus`.
3. Create a `SkillSet` for the detonation skill and add the `StackingSupport`.
4. Create a separate applicator `SkillSet`. The applicator can be any normal
   projectile or AOE set and can be reached through any normal trigger link.
5. Wire the applicator set to the stacking set with `StackTrigger`.

Example:

```text
[SkillSetSlot: SetA]
[TriggerLinkSlot: OnImpactAoe]
[SkillSetSlot: SetB_Applicator]
[TriggerLinkSlot: StackTrigger]
[SkillSetSlot: StackSet_Detonation + StackingSupport]
```

The applicator remains a plain `RuntimeProjectileDefinition` or
`RuntimeAoeDefinition`, so it still composes with `ProjectileIntervalSpawn`,
`AoeIntervalSpawn`, `OnImpactAoe`,
`OnImpactProjectile`, and `OnAoeHitSpawn`. Only `StackTrigger` consumes the
stacking detonation runtime.

**Contribution model.** With `stackThreshold = 5` and `stacksPerHit = 1`, each
applicator hit banks one stack, so five hits reach the threshold. With
`stacksPerHit = 2`, each hit banks two stacks, so the threshold is reached in
`ceil(5/2) = 3` hits. At threshold the detonation fires the summed total, so stat
changes mid-build apply per stack and stay correct across support or set swaps.

**Burst detonation.** Accrual and threshold detonation run in separate systems,
so a target can bank several thresholds' worth of stacks in one frame (a dense
applicator volley, or `stacksPerHit` overshoot). `StatusProcessSystem` fires one
detonation per full threshold banked, all in the same frame — `floor(count /
threshold)` detonations — and keeps the sub-threshold remainder banked for the
next hit. This is capped per target per frame so detonation spawn ids stay
bounded and unique; overflow beyond the cap rolls to the next frame.

**Composition.** To chain after a detonation AOE, put a normal AOE trigger after
the detonation set and then wire the next applicator to its own stacking set:

```text
[SkillSetSlot: FirstApplicator]
[TriggerLinkSlot: StackTrigger]
[SkillSetSlot: FirstDetonation + StackingSupport]
[TriggerLinkSlot: OnAoeHitSpawn]
[SkillSetSlot: SecondApplicator]
[TriggerLinkSlot: StackTrigger]
[SkillSetSlot: SecondDetonation + StackingSupport]
```
### Creating a Skill Set

1. `Assets > Create > PlayGround > Skills > Skill Set`.
2. Assign one Skill to the `skill` field.
3. Add Skill Supports to the `supports` array in desired order.

### Building the Slot List

The `PlayerLoadout.slots` list uses `[SerializeReference]` — add entries via
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
[SkillSetSlot: SetA] [TriggerLinkSlot: OnImpactAoe] [SkillSetSlot: SetB]
```

**Deep chain:**
```
[SkillSetSlot: SetA] [TriggerLinkSlot: ProjectileIntervalSpawn] [SkillSetSlot: SetB]
[SkillSetSlot: SetB] [TriggerLinkSlot: OnImpactAoe] [SkillSetSlot: SetC]
```
SetB appears as both effect (of SetA) and cause (for SetC). It is compiled as
a triggered-only set — not player-input-driven.

### Equipping on the Player

1. Create a `PlayerLoadout` SO (`Assets > Create > PlayGround > Skills > Player Loadout`).
2. Add `SkillSetSlot` and `TriggerLinkSlot` entries to `slots` in order.
3. Confirm `maxRootSets` covers the number of independent root skills.
4. Assign the `PlayerLoadout` SO to `PlayerSkillDriver.loadout` on the player prefab.

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
Slots: [SetA: [MultipleProjectiles(count=5, spread=40°), Piercing(pierce=2)] + MagicBullet]

Root: SetA
```

Fires five piercing magic bullets spread across 40 degrees.

---

### Chained: one trigger link

```
Slots: [SetA: [MultipleProjectiles, Piercing] + MagicBullet]
       [OnImpactAoe]
       [SetB: ArcaneBurst]

Parsed chain: SetA → OnImpactAoe → SetB
Root: SetA
```

Each magic bullet explodes into an arcane burst on impact. SetB has no
supports — the burst fires at its own base radius and damage, independent of
SetA.

---

### Deep chain: two trigger links

```
Slots: [SetA: [MultipleProjectiles, Piercing] + MagicBullet]
       [ProjectileIntervalSpawn(interval=0.2s, count=2, spread=45°)]
       [SetB: MagicBullet]
       [OnImpactAoe]
       [SetC: ArcaneBurst]

Parsed chains: SetA → ProjectileIntervalSpawn → SetB
               SetB → OnImpactAoe → SetC
Effects: {SetB, SetC}
Root: SetA
```

SetA fires many piercing bullets. Each projectile periodically spawns child
bullets (SetB) while in flight. Each child bullet releases an arcane burst
(SetC) on impact.

SetB appears at index 2 (effect of SetA) and index 3 (cause for SetC). It is
triggered only — never fired by player input.

SetB's bullet has no pierce — SetA's pierce support does not carry over.
SetC's burst radius is its own authored value, unaffected by SetA or SetB.

---

### Stacking chain

```
Slots: [SetA: MagicBullet]
       [OnImpactAoe]
       [SetB: VolatileApplicatorAoe]
       [StackTrigger]
       [SetC: VolatileDetonationAoe + StackingSupport]
```

SetA releases SetB through a normal impact-AOE link. SetB remains a plain AOE
applicator, but `StackTrigger` bakes SetC's stack payload into SetB at compile
time. When SetB hits a target, `HitApplyFinalizeSystem` adds stacks under SetC's
minted debuff key. `StatusProcessSystem` handles the threshold detonation using
the summed contribution.
---

## Set Isolation Rules

- Stat Modifier Supports only modify the Skill in the same set.
- Trigger Links carry no stats or behavior into the target set.
- The target set's Skill and Supports fully determine triggered behavior.
- Stat inheritance across sets does not exist.
- A set can appear as the effect in multiple chains (multiple causes pointing
  to it). It is compiled independently for each — edits propagate to all
  compiled instances on next recompile.

---

## Chain Depth and Trigger Scope

Triggers fire strictly by **slot position**, not by skill identity. A trigger chain only
connects the specific cause-effect pair defined by adjacent slots — no cross-chain
firing, no transitive propagation beyond the authored depth.

```
Skill1 → Trigger → Skill2
```

Only Skill1 fires Skill2 via the trigger. Skill2 never re-fires itself through that
trigger, even if it hits the same targets under the same conditions.

```
Skill1 → TriggerA → Skill2 → TriggerB → Skill3
```

Skill1 fires Skill2 via TriggerA. Skill2 fires Skill3 via TriggerB. No other
trigger relationships exist. Skill3 has no outgoing trigger, regardless of what
SkillSets it shares identity with.

### Self-Referential Chains

A SkillSet asset may appear as both cause and effect in the same chain:

```
[SkillSetSlot: SetA] [TriggerLinkSlot: OnAoeHitSpawn] [SkillSetSlot: SetA]
```

This is valid. SOs are configuration templates, not instances. Each slot is always an
**independent compilation unit** — two slots that reference the same asset still produce
two separate `RuntimeSkillDefinition` instances, just as two slots with different assets
would. Instance identity is determined by slot position, not asset identity.

The cause slot compiles normally with its outgoing trigger wired. The effect slot
compiles with an empty chain list. This is a **recursion guard** in the compiler: when
the compiler detects it would recurse into the same SkillSet object it is already
compiling (cause and effect share the same SO reference), it passes an empty chain list
for that pass to prevent an infinite loop. The effect instance produced is still fully
independent — it just carries no outgoing trigger setup.

Practical use: one applicator can spawn another compiled instance of the same
applicator set through a normal trigger. If each instance points through
`StackTrigger` to a stacking set, each compiled detonation receives its own
debuff key and snapshots, so accumulator state stays independent.

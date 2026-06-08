# Skill System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Concepts

**Skill** — an active spell or attack. Defines what is spawned: a projectile,
an AOE, a beam. Owns base visual, collision shape, and behavior data. A Skill
slotted alone fires with base behavior and no augmentation.

**Additive Support** — augments the Skill in the same set. Modifies count,
pierce, speed, tracking, damage, spread, or other behavior fields. Only ever
affects the one Skill it shares a set with. No cross-set influence.

**Skill Set** — the unit of authoring. Contains one or more Skills and zero or
more Additive Supports. Self-contained: its Skills are only modified by its own
Supports. Skill Sets have no knowledge of what triggers them or what they
trigger.

**Trigger Link** — external wiring between two Skill Sets. Owned by the
loadout, not by either set. Defines the source set whose events fire the
trigger, the target set that fires when triggered, the trigger condition, and
any trigger-specific parameters.

**Player Loadout** — owns root Skill Sets (those fired by player input), all
Trigger Links between sets, and the input bindings for root sets.

---

## Architecture

The skill system drives the combat runtime without knowing about `ProjectileRoot`,
`AoeRoot`, or any internal config types. A translation layer sits between the two.
The skill system only crosses the boundary through that layer. The player-facing
authoring surface is limited to skill system types.

```
┌─────────────────────────────────┐
│  Layer 1: Equipment state       │
│  Skill, AdditiveSupport,        │
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
│  ProjectileRoot, AoeRoot,       │
│  ECS systems, MonoBehaviours    │
└─────────────────────────────────┘

Layer 1.5 (PlayerStatAggregator) — stateless utility used by Layer 1. Not a chain tier.
Layer 2.5 (SkillSpawnTranslator) — stateless utility used by Layer 2. Not a chain tier.
```

### Layer 1: Equipment State

Types: `Skill`, `AdditiveSupport`, `SkillSet`, `TriggerLink`, `PlayerLoadout`

- Owns all stat sources: base skill stats, supports, items, buffs, character level
- `PlayerLoadout` is live mutable equipment state — not just an authoring template
- `Skill` SOs are immutable authored templates; `PlayerLoadout` holds mutable slot references into them
- Save/load serializes slot references, not compiled runtime trees
- Emits change events when equipment or stat sources change (consumed by Layer 1.5)
- No per-frame stat math; scaling is rebaked on equipment swaps, buff changes, level up, etc.

### Layer 1.5: Stat Resolution

Types: `PlayerStatSnapshot`, `PlayerStatAggregator`

Stateless utility — not a chain tier. Pure aggregation function: inputs in, flat snapshot
out, no stored state. Collapses all Layer 1 stat sources into a single flat multiplier set.
Owns no data — all sources come from Layer 1.

`PlayerStatSnapshot` fields:
- `castSpeedMultiplier`
- `damageMultiplier`

Baking:
- `recoveryTime = baseRecoveryTime * castSpeedMultiplier`
- `damage = baseDamage * damageMultiplier`

### Layer 2: Orchestration

Types: `PlayerSkillDriver`, `SkillSlotState`, `SkillSetCompiler`

- Reads the flat `PlayerStatSnapshot` from Layer 1.5 — does not compute stats
- Compiles `SkillSet` + snapshot into `RuntimeSkillDefinition` trees whenever stats change
- After compile: registers `BasicAttackPrefab` templates with `ProjectileRoot` and
  `AoeTypeDefinition` entries with `AoeRoot`; stores resolved type IDs into compiled
  definitions. Re-registration on equip change is safe — roots deduplicate by reference.
- Owns `SkillSlotState` per root slot: tracks cooldown elapsed time, gates input-driven casts
- On player input: checks slot cooldown; if ready, calls `SkillSpawnTranslator` and resets timer
- On Layer 1 change: recompiles affected paths, re-registers types, updates slot `recoveryTime`
- Holds scene-side references to `ProjectileRoot`, `AoeRoot`, `AudioManager` — internal wiring only

`SkillSlotState` per root slot:
- `elapsedSinceLastFire` — ticked each frame, reset on successful fire
- `recoveryTime` — copied from `RuntimeSkillDefinition` whenever stats change
- `IsReady` — `elapsedSinceLastFire >= recoveryTime`
- `CooldownProgress` — 0..1, for UI

### Layer 2.5: Translation

Types: `SkillSpawnTranslator`

Stateless utility — not a chain tier. Both roots require visual types to be pre-registered
before any spawn; they bake sprite mesh, material, and VFX handlers at registration time
and return a type ID used in spawn commands. IDs are generated inside the roots; roots
deduplicate (re-registering same definition returns the existing ID). Type ID resolution
is state — it belongs in Layer 2, not here.

`SkillSpawnTranslator` takes a `RuntimeSkillDefinition` with type IDs already resolved
by Layer 2 + origin + aim; submits spawn request to the appropriate root; returns nothing.

---

## Skill Set Structure

```csharp
class SkillSet : ScriptableObject {
    Skill skill;
    AdditiveSupport[] supports;
    float baseRecoveryTime;          // base cooldown in seconds; scaled by castSpeedMultiplier at compile time
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
and `LingeringAoeSkill`, each holding their typed definition inline. Regular and
lingering AOE skills derive from the same AOE skill base. The SO is never mutated
at runtime.

```csharp
abstract class Skill : ScriptableObject {
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
 └─ behavior:  sizeMultiplier, damage, count, spawnAtAimPosition,
               directDamageEnabled
```

Regular AOE content compiles as pulse AOE. It does not expose lifetime or tick
interval, and the runtime receives `0` for both timing fields.

### LingeringAoeDefinition

```
LingeringAoeDefinition
 ├─ prefab:    LingeringAoePrefab ← sprite, material, hitbox collider, particle effects
 └─ behavior:  sizeMultiplier, damage, lifetimeSeconds, tickIntervalSeconds,
               count, spawnAtAimPosition, directDamageEnabled
```

---

## Additive Supports

Each Additive Support is a ScriptableObject with an `Apply` method that
mutates a runtime definition copy. Supports are composable and order-independent
unless two supports modify the same field, in which case application order
follows slot order left to right.

```csharp
abstract class AdditiveSupport : ScriptableObject {
    public abstract SkillDefinitionTags SupportedSkillTags { get; }
    public abstract void Apply(SkillDefinition def);
}
```

Examples:

| Support | Fields it modifies |
|---|---|
| Multiple Projectiles | `count`, `spreadDegrees` |
| Piercing | `pierceCount`, `repeatHitCooldown` |
| Homing | `trackingEnabled`, `trackingRange`, `trackingTurnSpeed` |
| Concentrated Effect | `sizeMultiplier` (AOE), `damage` |
| Faster Projectiles | `speed`, `lifetime` |
| Added Damage | `damage` |

Supports also declare compatible skill tags:

| Support | Compatible tags |
|---|---|
| Multiple Projectiles | `Projectile` |
| Piercing | `Projectile` |
| Homing | `Projectile` |
| Faster Projectiles | `Projectile` |
| Added Damage | `Projectile`, `Aoe` |
| Concentrated Effect | `Aoe` |

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
slots: [SetA | ChildSpawn | SetB | OnImpactAoe | SetC]

chains:  SetA → ChildSpawn → SetB
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

**ChildSpawnTrigger**

The cause projectile periodically spawns child projectiles from the effect set
while in flight. Compiles a `RuntimeChildSpawnSetup` onto the cause
`RuntimeProjectileDefinition`. Effect must compile to a `RuntimeProjectileDefinition`.

```csharp
class ChildSpawnTrigger : TriggerLink {
    float intervalSeconds;
    float intervalJitterPercent;
    int spawnCount;
    float sideSpreadDegrees;
}
```

Compatible tags: source `Projectile`, target `Projectile`.
`intervalJitterPercent` is clamped from `0` to `100` and converted at compile
time to jitter seconds using `intervalSeconds * intervalJitterPercent / 100`.
The compiled jitter seconds are passed into the projectile ECS child-spawner and
applied when scheduling child-spawn intervals.

**OnImpactAoeTrigger**

Fires the effect set as an AOE centered at the cause projectile's impact point.
Compiles into `RuntimeProjectileDefinition.ImpactAoeDefinition`. Effect must
compile to a `RuntimeAoeDefinition`.

```csharp
class OnImpactAoeTrigger : TriggerLink { }
```

Compatible tags: source `Projectile`, target `Aoe`.

**OnStackTrigger**

Fires the effect set as an AOE when a debuff stack threshold is reached on the
hit target. Effect must compile to a `RuntimeAoeDefinition`.

```csharp
class OnStackTrigger : TriggerLink {
    MobDebuffStatus debuffStatus;
    int stacksPerHit;
    int stackThreshold;
}
```

Compatible tags: source `Projectile`, target `Aoe`.

Trigger links are also tag-validated but not blocked. A ChildSpawn trigger from
a projectile set to an AOE set is allowed in the loadout, but no child spawn
setup is compiled and validation returns a warning.

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
parseChains(slots) → TriggerChain[]:
    for each SkillSetSlot at index i:
        if slots[i+1] is TriggerLinkSlot and slots[i+2] is SkillSetSlot:
            emit TriggerChain { cause=slots[i], link=slots[i+1], effect=slots[i+2] }

compile(SkillSet set, allChains, snapshot) → RuntimeSkillDefinition:
    def = set.skill.Definition.DeepCopy()
    for each support in set.supports:
        support.Apply(def)
    runtime = BuildRuntime(def, snapshot)       // ProjectileDefinition → RuntimeProjectileDefinition, etc.
    runtime.RecoveryTime = set.BaseRecoveryTime * snapshot.CastSpeedMultiplier
    for each chain in allChains where chain.cause == set:
        if chain.link is ChildSpawnTrigger:
            compile chain.effect recursively → RuntimeProjectileDefinition
            bake RuntimeChildSpawnSetup onto runtime.ChildSpawnSetup
        if chain.link is OnImpactAoeTrigger:
            compile chain.effect recursively → RuntimeAoeDefinition
            set runtime.ImpactAoeDefinition
        if chain.link is OnStackTrigger:
            compile chain.effect recursively → RuntimeAoeDefinition
            set runtime.StackTriggerSetup
    return runtime

compileLoadout(PlayerLoadout loadout):
    snapshot = PlayerStatAggregator.Aggregate(loadout)
    chains = parseChains(loadout.slots)
    effects = { chain.effect for each chain }
    rootSets = [ slot.skillSet for each SkillSetSlot in slots if slot.skillSet not in effects ]
    for each rootSet:
        compiledSlots[i] = compile(rootSet, chains, snapshot)
    RegisterProjectileTypes()   // walk compiled trees; call projectileRoot.RegisterTemplate per unique prefab
    RegisterAoeTypes()          // walk compiled trees; call aoeRoot.RegisterType per unique AoeTypeDefinition
```

The compiled runtime tree feeds directly into the existing spawn request and
ECS simulation path. The ECS systems receive snapshots and are unaware of the
skill system above them.

### Type Registration

After compilation, `PlayerSkillDriver` recursively walks all compiled trees:
- Projectile prefabs: each unique `BasicAttackPrefab` is registered once with
  `ProjectileRoot.RegisterTemplate`; the returned `TypeId` is stored on the
  `RuntimeProjectileDefinition`.
- AOE definitions: each `RuntimeAoeDefinition` is converted to an
  `AoeTypeDefinition` and registered with `AoeRoot.RegisterType`; the returned
  `TypeId` is stored. Both roots deduplicate — re-registering the same reference
  returns the existing ID.

Registration re-runs via `BindAoeRoot` whenever `AoeRoot` is wired after compile.

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

Player-facing types only — these are the only types that appear in loadout
editors, skill slot UIs, or player save data:

| Type | Role |
|---|---|
| `Skill` | Authored baseline for one spell or attack; carries base stat fields |
| `AdditiveSupport` | Augments the skill in its set |
| `SkillSet` | One skill plus its supports |
| `SkillSetSlot` | Slot entry wrapping a `SkillSet` in the loadout list |
| `TriggerLinkSlot` | Slot entry wrapping a `TriggerLink` in the loadout list |
| `TriggerLink` | Trigger condition and parameters; no source/target references |
| `PlayerLoadout` | Ordered slot list; live equipment state |

Internal runtime types (`ProjectileRoot`, `AoeRoot`, ECS systems) are not
exposed to the player-facing authoring surface.

### Asset Locations

- `Assets/ScriptableObjects/Skills/`: Skill assets
- `Assets/ScriptableObjects/Skills/Supports/`: Additive Support assets
- `Assets/ScriptableObjects/Skills/Sets/`: Skill Set assets

### Creating a Skill

1. `Assets > Create > PlayGround > Skills > Projectile Skill` or `AOE Skill`.
2. Assign sprite, material, and collision shape fields.
3. Set base behavior values (speed, damage, lifetime, etc.).

### Creating a Skill Set

1. `Assets > Create > PlayGround > Skills > Skill Set`.
2. Assign one Skill to the `skill` field.
3. Set `baseRecoveryTime` (cooldown in seconds before the set can fire again).
4. Add Additive Supports to the `supports` array in desired order.

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
[SkillSetSlot: SetA] [TriggerLinkSlot: ChildSpawn] [SkillSetSlot: SetB]
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

### Augmented: additive supports only

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
       [ChildSpawn(interval=0.2s, count=2, spread=45°)]
       [SetB: MagicBullet]
       [OnImpactAoe]
       [SetC: ArcaneBurst]

Parsed chains: SetA → ChildSpawn → SetB
               SetB → OnImpactAoe → SetC
Effects: {SetB, SetC}
Root: SetA
```

SetA fires many piercing bullets. Each spawns child bullets (SetB) while in
flight. Each child bullet releases an arcane burst (SetC) on impact.

SetB appears at index 2 (effect of SetA) and index 3 (cause for SetC). It is
triggered only — never fired by player input.

SetB's bullet has no pierce — SetA's pierce support does not carry over.
SetC's burst radius is its own authored value, unaffected by SetA or SetB.

---

## Set Isolation Rules

- Additive Supports only modify the Skill in the same set.
- Trigger Links carry no stats or behavior into the target set.
- The target set's Skill and Supports fully determine triggered behavior.
- Stat inheritance across sets does not exist.
- A set can appear as the effect in multiple chains (multiple causes pointing
  to it). It is compiled independently for each — edits propagate to all
  compiled instances on next recompile.

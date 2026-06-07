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

`Skill` is abstract. Concrete types are `ProjectileSkill` and `AoeSkill`,
each holding their typed definition inline. The SO is never mutated at runtime.

```csharp
abstract class Skill : ScriptableObject {
    public abstract SkillDefinition Definition { get; }
}

[CreateAssetMenu(menuName = "PlayGround/Skills/Projectile Skill")]
sealed class ProjectileSkill : Skill {
    ProjectileDefinition definition;
}

[CreateAssetMenu(menuName = "PlayGround/Skills/AOE Skill")]
sealed class AoeSkill : Skill {
    AoeDefinition definition;
}
```

`SkillDefinition` is an abstract serializable base class with a `DeepCopy()`
method. Concrete definitions are copied at compile time into a mutable runtime
instance. The SO template is never mutated.

Current Skill types and their definition roots:

| Skill type | SO type | Definition root |
|---|---|---|
| Projectile skill | `ProjectileSkill` | `ProjectileDefinition` |
| AOE skill | `AoeSkill` | `AoeDefinition` |

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

---

## Trigger Links

Trigger Links live on the `PlayerLoadout`, not on any Skill Set. Each link
names a source set, a target set, the trigger condition, and any
condition-specific parameters.

```csharp
[Serializable]
abstract class TriggerLink {
    public SkillSet source;
    public SkillSet target;
}
```

`TriggerLink` subclasses use `[SerializeReference]` on `PlayerLoadout.links`
so polymorphic instances serialize correctly in Unity. The compiler
pattern-matches on concrete subclass type to apply link behavior at compile time.

### Current Trigger Types

**ChildSpawnTrigger**

The source projectile periodically spawns child projectiles from the target set
while in flight. Compiles a `RuntimeChildSpawnSetup` onto the parent
`RuntimeProjectileDefinition`. Target must compile to a `RuntimeProjectileDefinition`.

```csharp
class ChildSpawnTrigger : TriggerLink {
    float intervalSeconds;
    int spawnCount;
    float sideSpreadDegrees;
}
```

**OnImpactAoeTrigger**

Fires the target set as an AOE centered at the projectile impact point. Compiles
into `RuntimeProjectileDefinition.ImpactAoeDefinition`. Target must compile to
a `RuntimeAoeDefinition`.

```csharp
class OnImpactAoeTrigger : TriggerLink { }
```

---

## Player Loadout

```csharp
class PlayerLoadout {
    List<SkillSet> rootSets;      // fired by player input; each has an input binding
    List<TriggerLink> links;      // all inter-set wiring
    int maxRootSets;
}
```

Root sets are those fired directly by player input. All other sets are trigger
targets — they do not appear in `rootSets` and are only reachable through
`links`. A Skill Set can be the target of multiple links from different sources
— it is compiled independently for each, so there is no shared mutable state.

---

## Compilation

At equip time (`PlayerSkillDriver.Start`) the loadout compiles each root set
into a `RuntimeSkillDefinition` tree via `SkillSetCompiler`. Compilation
traverses the link graph to build the full chain. It does not run per frame.

```
compile(SkillSet set, allLinks, snapshot) → RuntimeSkillDefinition:
    def = set.skill.Definition.DeepCopy()
    for each support in set.supports:
        support.Apply(def)
    runtime = BuildRuntime(def, snapshot)       // ProjectileDefinition → RuntimeProjectileDefinition, etc.
    runtime.RecoveryTime = set.BaseRecoveryTime * snapshot.CastSpeedMultiplier
    for each link in allLinks where link.source == set:
        if link is ChildSpawnTrigger:
            compile target recursively → RuntimeProjectileDefinition
            bake RuntimeChildSpawnSetup onto runtime.ChildSpawnSetup
        if link is OnImpactAoeTrigger:
            compile target recursively → RuntimeAoeDefinition
            set runtime.ImpactAoeDefinition
    return runtime

compileLoadout(PlayerLoadout loadout):
    snapshot = PlayerStatAggregator.Aggregate(loadout)
    for each rootSet in loadout.rootSets:
        compiledSlots[i] = compile(rootSet, loadout.links, snapshot)
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

player adds a TriggerLink SetA → SetB
→ links updated
→ recompile all root sets   // now includes the new outgoing link

player swaps SetA for a different SkillSet in a root slot
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
| `TriggerLink` | Wiring between sets (lives on `PlayerLoadout`) |
| `PlayerLoadout` | Root sets, trigger links, input bindings; live equipment state |

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

### Wiring Trigger Links

Trigger Links are authored on the `PlayerLoadout` SO, not on Skill Sets.
`PlayerLoadout.links` uses `[SerializeReference]` — add entries via the
Unity inspector using the managed reference picker.

1. Add a Trigger Link entry to `PlayerLoadout.links`.
2. Set `source` to the firing Skill Set.
3. Set `target` to the Skill Set that fires when triggered.
4. Set the trigger type and any type-specific parameters.

### Equipping on the Player

1. Create a `PlayerLoadout` SO (`Assets > Create > PlayGround > Skills > Player Loadout`).
2. Assign root Skill Sets to `rootSets`.
3. Confirm `maxRootSets` covers the desired count.
4. Wire Trigger Links between sets as needed.
5. Assign the `PlayerLoadout` SO to `PlayerSkillDriver.loadout` on the player prefab.

---

## Examples

In all examples, Sets and Links are separate. Sets have no knowledge of
each other.

### Simple: single skill, no supports

```
Sets:
  SetA: MagicBullet

Links:
  (none)

Root: SetA → primary fire
```

Fires one magic bullet at base speed and damage.

---

### Augmented: additive supports only

```
Sets:
  SetA: [MultipleProjectiles(count=5, spread=40°), Piercing(pierce=2)] + MagicBullet

Links:
  (none)

Root: SetA → primary fire
```

Fires five piercing magic bullets spread across 40 degrees.

---

### Chained: one trigger link

```
Sets:
  SetA: [MultipleProjectiles, Piercing] + MagicBullet
  SetB: ArcaneBurst

Links:
  SetA --OnImpactAoe--> SetB

Root: SetA → primary fire
```

Each magic bullet explodes into an arcane burst on impact. SetB has no
supports — the burst fires at its own base radius and damage, independent of
SetA.

---

### Deep chain: two trigger links

```
Sets:
  SetA: [MultipleProjectiles, Piercing] + MagicBullet
  SetB: MagicBullet
  SetC: ArcaneBurst

Links:
  SetA --ChildSpawn(interval=0.2s, count=2, spread=45°)--> SetB
  SetB --OnImpactAoe--> SetC

Root: SetA → primary fire
```

SetA fires many piercing bullets. Each spawns child bullets (SetB) while in
flight. Each child bullet releases an arcane burst (SetC) on impact.

SetB's bullet has no pierce — SetA's pierce support does not carry over.
SetC's burst radius is its own authored value, unaffected by SetA or SetB.

---

## Set Isolation Rules

- Additive Supports only modify the Skill in the same set.
- Trigger Links carry no stats or behavior into the target set.
- The target set's Skill and Supports fully determine triggered behavior.
- Stat inheritance across sets does not exist.
- A set used as a trigger target from multiple sources is compiled
  independently for each source — edits to the set propagate to all
  compiled instances on next recompile.

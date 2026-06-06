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

Current implementation uses exactly one Skill per set. Multi-skill set
behaviour is TBD — see [Multi-Skill Sets](#multi-skill-sets).

**Trigger Link** — external wiring between two Skill Sets. Owned by the
loadout, not by either set. Defines the source set whose events fire the
trigger, the target set that fires when triggered, the trigger condition, and
any trigger-specific parameters.

**Player Loadout** — owns root Skill Sets (those fired by player input), all
Trigger Links between sets, and the input bindings for root sets.

---

## Skill Set Structure

```csharp
class SkillSet : ScriptableObject {
    Skill skill;                     // single skill — current implementation
    List<AdditiveSupport> supports;
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

```csharp
class Skill : ScriptableObject {
    SkillDefinition definition;  // visual, collision, base behavior
}
```

`SkillDefinition` is a value type — it can be deep-copied at equip time into a
mutable runtime instance. The SO is never mutated at runtime.

Current Skill types and their definition roots:

| Skill type | Definition root |
|---|---|
| Projectile skill | `ProjectileDefinition` |
| AOE skill | `AoeDefinition` |
| Beam skill | `BeamDefinition` (future) |

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
 └─ behavior:  damage, lifetimeSeconds, tickIntervalSeconds,
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
    public abstract HitEffect BuildHitEffect(SkillDefinition compiledTarget);
}
```

### Current Trigger Types

**ChildSpawnTrigger**

The source projectile periodically spawns child projectiles from the target set
while in flight.

```csharp
class ChildSpawnTrigger : TriggerLink {
    float intervalSeconds;
    int spawnCount;
    float sideSpreadDegrees;
}
```

**OnHitTrigger**

Fires the target set at the hit position when the source projectile or AOE
records a hit.

```csharp
class OnHitTrigger : TriggerLink { }
```

**OnImpactAoeTrigger**

Fires the target set as an AOE centered at the impact point.

```csharp
class OnImpactAoeTrigger : TriggerLink { }
```

**OnKillTrigger**

Fires the target set when the source hit kills the target.

```csharp
class OnKillTrigger : TriggerLink { }
```

**OnExpireTrigger**

Fires the target set at the source projectile or AOE position when its
lifetime ends.

```csharp
class OnExpireTrigger : TriggerLink { }
```

**StackingTrigger**

Applies stacks to hit targets. Fires the target set when a target's stack count
reaches the threshold.

```csharp
class StackingTrigger : TriggerLink {
    int stackThreshold;
    int stacksPerHit;
}
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

At equip time the loadout compiles each root set into a runtime definition
tree. Compilation traverses the link graph to build the full chain. It does not
run per frame.

```
compile(SkillSet set, List<TriggerLink> allLinks) → RuntimeSkillDefinition:
    def = set.skill.definition.DeepCopy()
    for each support in set.supports:
        support.Apply(def)
    for each link in allLinks where link.source == set:
        compiledTarget = compile(link.target, allLinks)
        def.onHit.Add(link.BuildHitEffect(compiledTarget))
    return def

compileLoadout(PlayerLoadout loadout):
    for each rootSet in loadout.rootSets:
        runtimeDef = compile(rootSet, loadout.links)
        attack.Equip(runtimeDef)
    AoeRoot.RefreshRegistrations(allCompiledTrees)
```

`ChildSpawnTrigger.BuildHitEffect` produces a `ChildSpawnerSettings` entry on
the parent definition using the compiled child definition.

`OnImpactAoeTrigger.BuildHitEffect` produces a `SpawnAoeOnHitEffect` pointing
at the compiled target definition.

`StackingTrigger.BuildHitEffect` produces an `ApplyStacksEffect` with an
embedded `OnThresholdEffect` that fires the compiled target definition.

The compiled runtime tree feeds directly into the existing spawn request and
ECS simulation path. The ECS systems receive snapshots and are unaware of the
skill system above them.

### AOE Type Registration

After compilation, all compiled trees are traversed to discover referenced
`AoeDefinition` instances. Each is registered with the appropriate `AoeRoot`.
Registration re-runs whenever the compiled trees change.

---

## Runtime Modification

The compiled tree is a mutable runtime instance. SO templates are never
touched after compilation.

Mid-game changes re-compile the affected paths:

```
player adds a Support to SetA
→ recompile(SetA, links)
→ AoeRoot.RefreshRegistrations(newTree)
→ attack component receives new runtime definition

player adds a TriggerLink SetA → SetB
→ links updated
→ recompile(SetA, links)   // now includes the new outgoing link

player swaps SetA for a different SkillSet in a root slot
→ recompile new root set with all links that reference it as source
```

Recompile cost is proportional to the depth of the outgoing link graph from
the changed set — typically two or three levels deep.

---

## Authoring

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
3. Add Additive Supports to the `supports` list in desired order.

### Wiring Trigger Links

Trigger Links are authored on the `PlayerLoadout` component, not on Skill Sets.

1. Add a Trigger Link entry to `PlayerLoadout.links`.
2. Set `source` to the firing Skill Set.
3. Set `target` to the Skill Set that fires when triggered.
4. Set the trigger type and any type-specific parameters.

### Equipping on the Player

1. Assign root Skill Sets to `PlayerLoadout.rootSets`.
2. Confirm `maxRootSets` covers the desired count.
3. Assign an input binding to each root set.
4. Wire Trigger Links between sets as needed.

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

### Stacking explosion

```
Sets:
  SetA: [MultipleProjectiles] + MagicBullet
  SetB: ArcaneBurst

Links:
  SetA --StackingTrigger(threshold=5, stacksPerHit=1)--> SetB

Root: SetA → primary fire
```

Each bullet applies one stack per hit. At five stacks the target releases an
arcane burst. SetB's explosion damage and radius are fully independent of SetA.

---

### Shared trigger target

```
Sets:
  SetA: [MultipleProjectiles] + MagicBullet
  SetB: [Piercing] + PoisonArrow
  SetC: ArcaneBurst

Links:
  SetA --OnImpactAoe--> SetC
  SetB --OnKill--> SetC

Root: SetA → primary fire
      SetB → secondary fire
```

SetC is triggered by both SetA (on impact) and SetB (on kill). It is compiled
independently for each source — its runtime definition is not shared.

---

## Multi-Skill Sets

Because Trigger Links are external to Skill Sets, the single-skill restriction
is not architecturally required. Multi-skill sets are not currently implemented
but are a planned design space.

### Firing mode candidates

**Fire together** — set fires, every ready skill in the set fires simultaneously.
Each skill tracks its own recovery independently. Feels like a passive group
rather than a single cast. Skills never block each other.

**Cycle** — set fires, advances to the next skill in sequence, fires it, then
waits for that skill's recovery before advancing again. Creates a learnable
rhythm; authoring order is meaningful.

Cycle is the stronger design candidate because ordering becomes an expressive
authoring decision and players can build around the rhythm. Open question: if
a skill in the cycle misfires (e.g. AOE cast with no valid targets), does the
cycle advance or hold?

### Unresolved: support binding with multiple skills

The current support model applies each support to the set's one Skill. With
multiple skills per set, it is not yet decided which skills each support
modifies:

- **Apply to all** — every support applies to every skill in the set.
  Simple, but type-mismatched supports (e.g. an AOE-only support on a
  projectile skill) produce meaningless or undefined results.
- **Per-skill binding** — supports are explicitly bound to one skill within
  the set at authoring time. Expressive but reintroduces internal set
  structure.
- **Type-filtered** — each support declares compatible `SkillDefinition`
  types and silently skips incompatible skills. Automatic but opaque.

This question must be resolved before multi-skill sets are implemented.
Until then, one Skill per set is enforced.

---

## Set Isolation Rules

- Additive Supports only modify the Skill in the same set.
- Trigger Links carry no stats or behavior into the target set.
- The target set's Skill and Supports fully determine triggered behavior.
- Stat inheritance across sets does not exist.
- A set used as a trigger target from multiple sources is compiled
  independently for each source — edits to the set propagate to all
  compiled instances on next recompile.

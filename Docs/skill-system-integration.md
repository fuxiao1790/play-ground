# Skill System Integration Plan

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Goal

The skill system (`SkillSet`, `TriggerLink`, `PlayerLoadout`) must drive the
combat runtime without knowing about `ProjectileAttack`, `AoeAttack`,
`ProjectileRoot`, `AoeRoot`, or any config ScriptableObjects. A translation
layer sits between the two. The skill system only crosses the boundary through
that layer. The player-facing authoring surface is limited to skill system
types.

```
┌─────────────────────────────────┐
│  Layer 1: Equipment state       │
│  Skill, AdditiveSupport,        │
│  SkillSet, TriggerLink,         │
│  PlayerLoadout                  │
│                                 │
│  Mutable. Live equipment state. │
└────────────────┬────────────────┘
                 │ aggregate
┌────────────────▼────────────────┐
│  Layer 1.5: Stat resolution     │
│  PlayerStatSnapshot             │
│                                 │
│  Pure logic. Stateless.         │
│  Inputs → flat snapshot.        │
└────────────────┬────────────────┘
                 │ compile(SkillSet, PlayerStatSnapshot)
┌────────────────▼────────────────┐
│  Orchestration                  │
│  PlayerSkillDriver              │
│  SkillSlotState[] (cooldowns)   │
│  RuntimeSkillDefinition[]       │
│                                 │
│  Owns runtime slot state.       │
│  Gates input → translation.     │
│  Evaluates trigger links.       │
└────────────────┬────────────────┘
                 │ Execute(def, origin, aim)
┌────────────────▼────────────────┐
│  Layer 2: Translation           │
│  ISkillExecutor (stateless)     │
│  IAoeTypeRegistry               │
│  ICombatEventBus                │
│                                 │
│  Pure logic. No mutable state.  │
│  def + position → spawn payload │
└────────────────┬────────────────┘
                 │ drives / listens to
┌────────────────▼────────────────┐
│  Combat runtime (internal)      │
│  ProjectileRoot, AoeRoot,       │
│  ECS systems, MonoBehaviours    │
└─────────────────────────────────┘
```

---

## Layer 1: Equipment State

`PlayerLoadout` is live equipment state, not just an authoring template. It is
mutable at runtime (player swaps sets, adds supports, wires new links). Each
mutation triggers recompile of affected paths.

`Skill` SOs are immutable authored templates. `PlayerLoadout` holds mutable
slot references into those templates. Save/load serializes the loadout slot
references, not the compiled runtime trees.

`Skill` and `SkillSet` carry base stat values:

- `baseRecoveryTime` — base cooldown before next fire. Modified by
  `PlayerStatSnapshot.castSpeedMultiplier` at compile time.
- `baseDamage` — base damage output. Modified by
  `PlayerStatSnapshot.damageMultiplier` at compile time.

No per-frame stat math lives below this layer. All stat scaling is baked at
compile time into `RuntimeSkillDefinition`.

---

## Layer 1.5: Stat Resolution

`PlayerStatSnapshot` is a flat value struct. Produced by a pure stateless
aggregation function — inputs in, snapshot out, no stored state.

```csharp
struct PlayerStatSnapshot {
    float castSpeedMultiplier;
    float damageMultiplier;
    // extended as new stat axes are added
}

static class PlayerStatAggregator {
    static PlayerStatSnapshot Aggregate(PlayerLoadout loadout, CharacterStats stats, ...);
}
```

The stat sources (level, buffs, items) live in Layer 1 or the orchestration
layer. The aggregator only collapses them — it owns nothing. `PlayerSkillDriver`
calls it and triggers recompile when the result changes.

Baking examples:
- `runtimeDef.recoveryTime = skill.baseRecoveryTime * snapshot.castSpeedMultiplier`
- `runtimeDef.damage = skill.baseDamage * snapshot.damageMultiplier`

---

## Layer 2: Translation

Pure logic. No MonoBehaviours, no Unity lifecycle. Accepts a fully baked
`RuntimeSkillDefinition` and translates it into data payloads the internal
combat systems consume.

### ISkillExecutor

Translates a compiled skill definition into spawn payloads for the internal
combat systems. Stateless — no timer, no mutable fields.

```csharp
interface ISkillExecutor {
    void Execute(RuntimeSkillDefinition def, Vector2 origin, Vector2 aimDirection);
}
```

Implementations:

- `ProjectileSkillExecutor` — wraps `ProjectileRoot`; submits projectile spawn requests
- `AoeSkillExecutor` — wraps `AoeRoot`; submits AOE spawn requests

The executor is created by `PlayerSkillDriver` at runtime based on the root
`Skill` definition type — the skill system never chooses the implementation
directly.

Cooldown tracking and rejection are not the executor's concern. See
`SkillSlotState` under `PlayerSkillDriver`.

### IAoeTypeRegistry

Accepts AOE type registrations from the skill system during compilation and
recompile. Hides `AoeRoot.aoeTypes` from the skill system.

```csharp
interface IAoeTypeRegistry {
    void Refresh(IEnumerable<AoeDefinition> defs);
}
```

`Refresh` is called after every compile or recompile. It diffs the incoming
set against current registrations and adds or removes as needed.

Implementation: `AoeTypeRegistryAdapter` — wraps one `AoeRoot`, translates
`AoeDefinition` instances into baked AOE type entries.

### ICombatEventBus

The runtime emits combat events through this interface. The skill system
subscribes to receive hits, kills, and expiries so `TriggerLink` conditions
can be evaluated.

```csharp
interface ICombatEventBus {
    event Action<HitEvent> OnHit;
    event Action<KillEvent> OnKill;
    event Action<ExpireEvent> OnExpire;
}

struct HitEvent {
    SkillSet sourceSet;
    Vector2 position;
    int targetId;
}

struct KillEvent {
    SkillSet sourceSet;
    Vector2 position;
    int targetId;
}

struct ExpireEvent {
    SkillSet sourceSet;
    Vector2 position;
}
```

`sourceSet` lets trigger link evaluation filter events to the correct source
without the bus knowing about trigger topology.

Implementation: `CombatEventRelay` — subscribes to hit replay callbacks on
`ProjectileRoot` and `AoeRoot`; tags each event with the originating
`SkillSet`; re-emits through the bus.

---

## New Types

### PlayerSkillDriver

Replaces `PlayerAttackLoadout` as the scene-side skill coordinator.
One MonoBehaviour on the player. Owns the full equip → aggregate → compile →
execute → event cycle.

Responsibilities:

- Holds authored `PlayerLoadout`
- On `Awake`: aggregates `PlayerStatSnapshot`, compiles all root sets, creates
  `ISkillExecutor` instances and `SkillSlotState` instances, calls
  `IAoeTypeRegistry.Refresh`, subscribes to `ICombatEventBus`
- On input: checks `slot.IsReady`; if ready calls `executor.Execute(compiledDef,
  origin, aimDir)` and resets `slot.elapsedSinceLastFire`
- On `ICombatEventBus` events: evaluates all `TriggerLink` entries whose
  `source` matches the event's `sourceSet`; if condition met, calls the
  triggered set's executor (bypasses cooldown — triggered sets are not
  player-input gated)
- On stat change: re-aggregates snapshot, recompiles affected paths

### SkillSlotState

Mutable runtime state for one equipped slot. Owned by `PlayerSkillDriver`.

```csharp
class SkillSlotState {
    float elapsedSinceLastFire;
    float recoveryTime;          // copied from RuntimeSkillDefinition at compile

    bool IsReady => elapsedSinceLastFire >= recoveryTime;
    float CooldownProgress => Mathf.Clamp01(elapsedSinceLastFire / recoveryTime);  // for UI
}
```

One `SkillSlotState` per root slot. Triggered sets have no slot state —
cooldown gating applies only to player-input-driven root sets. `PlayerSkillDriver`
ticks `elapsedSinceLastFire` each frame and resets it on successful fire.

References held by `PlayerSkillDriver`:

```csharp
class PlayerSkillDriver : MonoBehaviour {
    [SerializeField] PlayerLoadout loadout;
    [SerializeField] ProjectileRoot projectileRoot;   // internal; not skill-system type
    [SerializeField] AoeRoot aoeRoot;                 // internal; not skill-system type
    [SerializeField] AudioManager audioManager;
}
```

`projectileRoot` and `aoeRoot` are wiring concerns for `PlayerSkillDriver`
only. The skill system types (`SkillSet`, `TriggerLink`) never reference them.

### SkillExecutorFactory

Creates the correct `ISkillExecutor` implementation for a given
`RuntimeSkillDefinition` root type. Called by `PlayerSkillDriver` during
compile.

```csharp
static class SkillExecutorFactory {
    static ISkillExecutor Create(RuntimeSkillDefinition def,
                                 ProjectileRoot projectileRoot,
                                 AoeRoot aoeRoot);
}
```

---

## Changes to Existing Types

### ProjectileAttack, AoeAttack, ChildSpawningProjectileAttack

Deleted. Responsibilities absorbed:

- Recovery timer → executor (duration read from `RuntimeSkillDefinition`)
- Spawn request submission → executor
- Sound dispatch → executor
- `ChildSpawningProjectileAttack` behavior → `ChildSpawnTrigger` link compiled
  into `ProjectileDefinition`; no MonoBehaviour needed

### ProjectileConfig, AoeConfig

Deleted. Replaced by `ProjectileDefinition` and `AoeDefinition` fields on
`Skill` SOs. No migration path — existing config assets are discarded and
skills are authored fresh as `Skill` SOs.

### BasicAttackPrefab, BasicAoePrefab template prefabs

Kept. Role narrows to a visual and collision preset: sprite, material, hitbox
collider, and particle effects. Behavior data (speed, damage, count, etc.)
moves entirely onto the `Skill` SO definition fields. The prefab contributes
nothing to behavior. Runtime baking path is unchanged — collision shape and
render data are still baked from the prefab at load time.

### ProjectileRoot, AoeRoot

No structural changes. They gain:

- A tagging mechanism so hit replay callbacks carry a `SkillSet` identifier
  for `CombatEventRelay` to use
- `AoeRoot` gains a diff-based registration path used by
  `AoeTypeRegistryAdapter.Refresh`

### PlayerAttackLoadout

Deleted once `PlayerSkillDriver` is active.

---

## Authoring Surface (Player-Facing)

Only these types are part of the player-facing authoring surface. Nothing
below this list should appear in loadout editors, skill slot UIs, or player
save data.

| Type | Role |
|---|---|
| `Skill` | Authored baseline for one spell or attack; carries base stat fields |
| `AdditiveSupport` | Augments the skill in its set |
| `SkillSet` | One skill plus its supports |
| `TriggerLink` | Wiring between sets (lives on `PlayerLoadout`) |
| `PlayerLoadout` | Root sets, trigger links, input bindings; live equipment state |

Types deleted as part of this plan:

- `ProjectileAttack`
- `AoeAttack`
- `ChildSpawningProjectileAttack`
- `PlayerAttackLoadout`
- `ProjectileConfig` (SO)
- `AoeConfig` (SO)

Internal runtime types not exposed to the player (kept, not deleted):

- `ProjectileRoot`
- `AoeRoot`
- ECS systems

---

## Migration Order

### Step 1 — Define interfaces, event structs, and PlayerStatSnapshot

Create `ISkillExecutor`, `IAoeTypeRegistry`, `ICombatEventBus`, `HitEvent`,
`KillEvent`, `ExpireEvent`, `PlayerStatSnapshot`. Add `baseRecoveryTime` and
`baseDamage` fields to `Skill`. No behavior changes — these are empty contracts
and data fields.

### Step 2 — Implement CombatEventRelay

Subscribe to existing `ProjectileRoot` and `AoeRoot` hit replay callbacks.
Re-emit as typed bus events. Tag each event with a `SkillSet` reference passed
in from the executor at fire time.

### Step 3 — Implement ProjectileSkillExecutor and AoeSkillExecutor

Each wraps its root, accepts `RuntimeSkillDefinition`, translates to spawn
requests using the same logic currently in `ProjectileAttack` and `AoeAttack`.
Recovery timer lives on the executor; duration read from `def.recoveryTime`.

Verify: existing gameplay still works with executors wired manually, before
`PlayerSkillDriver` exists.

### Step 4 — Implement AoeTypeRegistryAdapter

Wrap `AoeRoot`. Implement `Refresh` as a diff against currently registered
types. Call from a test harness before wiring into the skill compiler.

### Step 5 — Implement SkillSet compiler

`SkillSetCompiler.Compile(SkillSet set, IEnumerable<TriggerLink> links, PlayerStatSnapshot snapshot)`
produces a `RuntimeSkillDefinition`. Additive supports apply. Stat scaling
(damage, recoveryTime) is baked using the snapshot. Trigger links build hit
effects pointing at compiled target definitions. AOE definitions collected and
passed to `IAoeTypeRegistry.Refresh`.

### Step 6 — Implement PlayerSkillDriver

Wire aggregation → compiler output → executors. Subscribe to `ICombatEventBus`.
Evaluate trigger links on events. Fire triggered sets through their executors.
`PlayerAttackLoadout` can be disabled on the player prefab once
`PlayerSkillDriver` is active.

### Step 7 — Author all skills as SkillSets

Create `Skill` SO assets directly. Do not port existing `ProjectileConfig` or
`AoeConfig` assets — discard them. Create `SkillSet` assets for all player
attacks. Wire `PlayerLoadout` with root sets and trigger links.

### Step 8 — Delete legacy types

Delete `ProjectileAttack`, `AoeAttack`, `ChildSpawningProjectileAttack`,
`PlayerAttackLoadout`, all `ProjectileConfig` assets, and all `AoeConfig`
assets. Delete the corresponding MonoBehaviour script files. Remove references
from all scenes and prefabs before deletion. `BasicAttackPrefab` and
`BasicAoePrefab` template prefabs are kept — strip any behavior-carrying
fields from them if present.

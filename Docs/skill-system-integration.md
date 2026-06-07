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

---

## Layer 1: Equipment State

Types: `Skill`, `AdditiveSupport`, `SkillSet`, `TriggerLink`, `PlayerLoadout`

Responsibilities:
- Owns all stat sources: base skill stats, supports, items, buffs, character level
- `PlayerLoadout` is live mutable equipment state — not just an authoring template
- `Skill` SOs are immutable authored templates; `PlayerLoadout` holds mutable slot references into them
- Save/load serializes slot references, not compiled runtime trees
- Emits change events when equipment or stat sources change (consumed by Layer 1.5)

Key fields on `Skill` / `SkillSet`:
- `baseRecoveryTime` — base cooldown before next fire; scaled by stat snapshot whenever stats change
- `baseDamage` — base damage output; scaled by stat snapshot whenever stats change

No per-frame stat math lives below this layer. Scaling is rebaked whenever Layer 1 state changes — equipment swaps, buff application, buff expiry, level up, etc.

---

## Layer 1.5: Stat Resolution

Stateless utility. Not a chain tier — used by Layer 1.

Types: `PlayerStatSnapshot`, `PlayerStatAggregator`

Responsibilities:
- Pure stateless aggregation function — inputs in, flat snapshot out, no stored state
- Collapses all Layer 1 stat sources (loadout, level, buffs, items) into a single flat multiplier set
- Recomputes when Layer 1 emits a change event; result is cached until next change
- Owns no data — all sources come from Layer 1

`PlayerStatSnapshot` fields (extended as stat axes are added):
- `castSpeedMultiplier`
- `damageMultiplier`

Baking:
- `recoveryTime = baseRecoveryTime * castSpeedMultiplier`
- `damage = baseDamage * damageMultiplier`

---

## Layer 2: Orchestration

Types: `PlayerSkillDriver`, `SkillSlotState`, `SkillSetCompiler`

Responsibilities:
- Reads the flat `PlayerStatSnapshot` from Layer 1.5 — does not compute stats
- Compiles `SkillSet` + snapshot into `RuntimeSkillDefinition` trees whenever stats change
- After compile: registers `BasicAttackPrefab` templates with `ProjectileRoot` and
  `AoeTypeDefinition` entries with `AoeRoot`; stores resolved type IDs into the compiled
  definitions. Re-registration on equip change is safe — roots deduplicate by reference.
- Owns `SkillSlotState` per root slot: tracks cooldown elapsed time, gates input-driven casts
- On player input: checks slot cooldown; if ready, calls `SkillSpawnTranslator` (Layer 2.5)
  with compiled definition (type IDs resolved) and resets timer
- On Layer 1 change: recompiles affected paths, re-registers types, updates slot `recoveryTime`
- Handles runtime rolls: damage variance, etc.
- Holds scene-side references to `ProjectileRoot`, `AoeRoot`, `AudioManager` — internal wiring only, not exposed to skill system types

`SkillSlotState` per root slot:
- `elapsedSinceLastFire` — ticked each frame, reset on successful fire
- `recoveryTime` — copied from `RuntimeSkillDefinition` whenever stats change
- `IsReady` — `elapsedSinceLastFire >= recoveryTime`
- `CooldownProgress` — 0..1, for UI

---

## Layer 2.5: Translation

Stateless utility. Not a chain tier — used by Layer 2.

Types: `SkillSpawnTranslator`

Both roots (`ProjectileRoot`, `AoeRoot`) require visual types to be pre-registered
before any spawn — they bake sprite mesh, material, and VFX handlers at registration
time and return a type ID used in spawn commands. This applies equally to projectiles
and AOEs. IDs are generated inside the roots at registration time; roots deduplicate
(re-registering same definition returns the existing ID). Type ID resolution is state
— it belongs in Layer 2, not here.

Responsibilities:
- `SkillSpawnTranslator` — takes `RuntimeSkillDefinition` with type IDs already resolved
  by Layer 2 + origin + aim; submits spawn request to appropriate root; returns nothing

---

## Changes to Existing Types

### ProjectileAttack, AoeAttack, ChildSpawningProjectileAttack

Deleted. Responsibilities absorbed:
- Spawn request submission → `SkillSpawnTranslator` (Layer 2.5)
- Sound dispatch → Layer 2 (orchestration)
- `ChildSpawningProjectileAttack` behavior → `ChildSpawnTrigger` link baked into `RuntimeProjectileDefinition`; no MonoBehaviour needed

### ProjectileConfig, AoeConfig

Deleted. Replaced by definition fields on `Skill` SOs. No migration path —
existing config assets are discarded and skills are authored fresh as `Skill` SOs.

### BasicAttackPrefab, BasicAoePrefab template prefabs

Kept. Role narrows to visual and collision preset only: sprite, material, hitbox
collider, particle effects. Behavior data moves entirely onto `Skill` SO fields.
Runtime baking path unchanged — collision shape and render data still baked from
prefab at load time.

### ProjectileRoot, AoeRoot

No structural changes. Both already support pre-registration and type-ID-based spawn
commands. Layer 2 drives registration for both; no new API needed on either root.

### PlayerAttackLoadout

Deleted once `PlayerSkillDriver` is active.

---

## Authoring Surface (Player-Facing)

Only these types appear in loadout editors, skill slot UIs, or player save data:

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

Internal runtime types — kept, not exposed to player:
- `ProjectileRoot`
- `AoeRoot`
- ECS systems

---

## Migration Order

Build bottom-up: Layer 2.5 first (builds against existing internals), then Layer 2,
then Layer 1.5 hookup, then Layer 1. Player stat system (Layer 1.5 real implementation)
is deferred — stub with identity values until that system exists.

### Step 1 — Runtime data types

- Create `RuntimeSkillDefinition` hierarchy: `RuntimeProjectileDefinition`, `RuntimeAoeDefinition`, etc.
- No behavior changes — data containers only

### Step 2 — Layer 2.5 translation function

- Implement `SkillSpawnTranslator` — static; takes `RuntimeSkillDefinition` with type IDs
  already resolved + origin + aim; submits spawn request to appropriate root
- Verify: call directly from a test harness; confirm spawn behavior matches existing internals

### Step 3 — Layer 2 orchestration

- Implement `SkillSlotState`
- Implement `SkillSetCompiler` — stub `PlayerStatSnapshot` with identity multipliers (all 1.0) for now
- Implement `PlayerSkillDriver` — after compile, registers templates/definitions with both roots,
  stores resolved type IDs into compiled definitions, holds definitions ready for
  `SkillSpawnTranslator` on input; wire to player prefab; disable `PlayerAttackLoadout`
- Verify: gameplay works end-to-end through the new path with stub stats

### Step 4 — Layer 1.5 stub and hookup

- Introduce `PlayerStatSnapshot` struct and `PlayerStatAggregator` static function
- Initial implementation reads only from `PlayerLoadout` (no external stat sources yet)
- Wire into Layer 2: Layer 1 change events trigger re-aggregation and recompile of affected paths
- Real `CharacterStats` integration deferred until that system exists

### Step 5 — Layer 1: author Skill SOs and wire loadout

- Create `Skill`, `AdditiveSupport`, `SkillSet`, `TriggerLink`, `PlayerLoadout` ScriptableObjects
- Add `baseRecoveryTime` and `baseDamage` fields to `Skill`
- Author `Skill` SO assets for all player attacks — do not port `ProjectileConfig`/`AoeConfig`, discard them
- Wire `PlayerLoadout` with root sets and trigger links

### Step 6 — Delete legacy types

- Delete `ProjectileAttack`, `AoeAttack`, `ChildSpawningProjectileAttack`, `PlayerAttackLoadout`
- Delete all `ProjectileConfig` and `AoeConfig` assets
- Remove all scene and prefab references before deletion
- Keep `BasicAttackPrefab` and `BasicAoePrefab` — strip behavior-carrying fields if present

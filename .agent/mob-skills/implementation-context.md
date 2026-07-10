# Implementation Context

## Architectural Decisions
- Mobs reuse the same owner-agnostic `SkillDriver` and `SkillLoadout` path as the player.
- Faction is owner-side state on `SkillDriver`; loadouts stay reusable by player or mobs.
- Use the unified scene `CombatRoot`; do not activate or introduce a separate mob projectile root.

## Global Invariants
- `CombatRoot` spawn APIs take `CombatFaction`; mob skills must spawn as `CombatFaction.Mob`.
- Friendly fire remains the existing target faction inequality gate.
- Mobs without a `SkillDriver` must keep existing wander behavior with no errors.

## Ownership Boundaries
- `MobRoot` owns per-mob update order and target acquisition for this v1 behavior.
- `SkillDriver` owns skill compilation, cooldown ticking, and spawn translation.
- `CombatTargetRegistry<ICombatTarget>` remains the managed target source for main-thread acquisition.

## Data Flow
- `MobRoot.Update` -> `SkillDriver.Tick` -> `SkillSpawnTranslator` -> `CombatRoot.Spawn*`.
- `MobRoot.BindCombatRoot` forwards the root to the optional `SkillDriver`.

## Lifecycle / Allocation Rules
- Resolve the optional `SkillDriver` once in `Awake` with `GetComponent<SkillDriver>()`.
- Keep per-frame target acquisition allocation-free; scan existing registry lists.
- Dead mobs return before skill driving and keep existing proxy deletion flow.

## ECS / Job / Threading Constraints
- Do not read `TargetSpatialHashSingleton` from main-thread mob AI.
- Do not change ECS spawn, collision, pooling, or target proxy systems.

## Determinism Requirements
- No new deterministic rules are introduced by this task set.

## Producer / Consumer Separation
- Do not widen combat damage events or spawn events.
- Mob offense produces normal skill spawn requests only through `SkillDriver`.

## Reused Mechanisms
- `SkillDriver`, `SkillLoadout`, `SkillSpawnTranslator`, `CombatRoot`, target registries, target proxies, `UnitStatSheet`.

## Introduced Mechanisms
- One optional `SkillDriver` reference on `MobRoot`.
- One simple first-valid enemy target scan.

## Validation Requirements
- Compile/build after code and prefab wiring.
- Search-confirm no new loadout assets were created.
- Search-confirm the three mob prefabs have `SkillDriver` with loadout, mob faction, and mob stat sheet.

## Files / Systems Mentioned By The Plan
- `Assets/Scripts/Mob/MobRoot.cs`
- `Assets/Scripts/Skills/SkillDriver.cs`
- `Assets/Scripts/System/Targets/CombatTargetRegistry.cs`
- `Assets/Scripts/System/Targets/ICombatTarget.cs`
- `Assets/Prefabs/Mobs/Bat.prefab`
- `Assets/Prefabs/Mobs/Skeleton.prefab`
- `Assets/Prefabs/Mobs/Slime.prefab`
- `Assets/ScriptableObjects/Skills/Loadout/ProjectileStackTriggerLoadout.asset`
- `Assets/ScriptableObjects/Mobs/MobStatSheet.asset`

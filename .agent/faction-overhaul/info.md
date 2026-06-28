---
name: faction-overhaul-exploration
description: Exploration findings for consolidating faction architecture from two per-faction CombatRoots to one unified root with faction as a component field
---

# Faction Overhaul Exploration Findings

## Current Design

The current faction architecture lives in:
- [Docs/layers/combat-bridge.md](../../Docs/layers/combat-bridge.md) — owns CombatRoot per firing faction
- [Docs/contracts/spawn-events-and-commands.md](../../Docs/contracts/spawn-events-and-commands.md) — spawn events carry faction field
- [Docs/layers/ecs-simulation.md](../../Docs/layers/ecs-simulation.md) — collision and simulation with faction awareness

## Key Findings

### 1. Current Two-Root Architecture

**Scene setup ([Assets/Scripts/Game/GameRoot.cs](../../Assets/Scripts/Game/GameRoot.cs#L13-L14)):**
- Two separate `CombatRoot` instances: `playerCombatRoot` and `mobCombatRoot`
- Each tagged with distinct GameplayTags (`PlayerProjectileRoot`, `MobProjectileRoot`)
- Registered in static `ByFaction[faction]` array ([Assets/Scripts/System/Common/CombatRoot.cs:31](../../Assets/Scripts/System/Common/CombatRoot.cs#L31))

**Root bindings ([Assets/Scripts/Game/GameRoot.cs:51-82](../../Assets/Scripts/Game/GameRoot.cs#L51-L82)):**
- `GameRoot` wires both roots to all consumers (mobs, player, spawner)
- Each faction's projectiles/AOEs registered to its own root's target registry
- Separate `BindCombatRoot` calls per root in `MobRoot` ([Assets/Scripts/Mob/MobRoot.cs:241](../../Assets/Scripts/Mob/MobRoot.cs#L241))

### 2. Shared ECS World (Already Unified ✓)

**World and scope ([Assets/Scripts/System/Common/CombatEcsComponents.cs:71-159](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L71-L159)):**
- Both roots share ONE ECS world via ref-counted `CombatEcsWorld.Acquire()`
- Both roots share ONE `CombatScope` entity via ref-counted `CombatScopeOwner`
- Scope owns buffers for both `ProjectileSpawnEvent` and `AoeSpawnEvent` across both factions

### 3. Faction as Component Field (Already Present ✓)

**Faction enum ([Assets/Scripts/System/Common/CombatScope.cs:17-22](../../Assets/Scripts/System/Common/CombatScope.cs#L17-L22)):**
- `CombatFaction: byte { None=0, Player=1, Mob=2 }`

**Target proxy faction ([Assets/Scripts/System/Common/CombatTargetProxy.cs:26-29](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L26-L29)):**
- `TargetFaction` component stores faction for all targets
- Set on creation ([Assets/Scripts/System/Common/CombatTargetProxy.cs:79](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L79))

**Spawn event faction ([Assets/Scripts/System/Common/CombatRoot.cs:272](../../Assets/Scripts/System/Common/CombatRoot.cs#L272)):**
- `ProjectileSpawnEvent` carries `Faction` field

### 4. Collision System Faction Filtering

**Projectile collision ([Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs:78-86](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L78-L86)):**
- Targets registered by faction in spatial hash: `CellKey(targetFactions[i].Value, cell.x, cell.y)`
- Projectiles only query cells keyed by their faction

**AOE collision ([Assets/Scripts/System/Aoe/AoeCollisionCore.cs:94-99](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs#L94-L99)):**
- Checks `identity.Faction == CombatFaction.None` → deactivates; uses faction-keyed spatial hash

**Projectile tracking ([Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs:56-59](../../Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs#L56-L59)):**
- Target acquisition uses faction-keyed spatial hash: `CellKey(faction, cell.x, cell.y)`

### 5. Spawn Registry and Reuse

**Spawn template registry ([Assets/Scripts/System/Common/CombatEcsComponents.cs:80-87](../../Assets/Scripts/System/Common/CombatEcsComponents.cs#L80-L87)):**
- Shared native maps for `ProjectileSpawnCommand` and `AoeSpawnCommand` across both factions
- Templates stored once, reusable regardless of source faction (Hash128 key is faction-agnostic)

**Active reuse ([Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs)):**
- Reuses disabled `Active` slots before cold creation
- Archetype-based reuse (projectile archetype matched regardless of who spawned it)

## System Integration Points Affected by Consolidation

1. **Scene root wiring** ([Assets/Scripts/Game/GameRoot.cs](../../Assets/Scripts/Game/GameRoot.cs))
   - Consolidate two root refs → one unified root
   - Update binding calls to handle both factions through single root

2. **Per-faction root registration** ([Assets/Scripts/System/Common/CombatRoot.cs:95-99](../../Assets/Scripts/System/Common/CombatRoot.cs#L95-L99))
   - Replace `ByFaction[faction] = this` with single root query/registration
   - Update `TryGetByFaction` to return unified root for both factions

3. **Target registry and proxy creation** ([Assets/Scripts/System/Common/CombatRoot.cs:50](../../Assets/Scripts/System/Common/CombatRoot.cs#L50), [CombatTargetProxy.cs:63](../../Assets/Scripts/System/Common/CombatTargetProxy.cs#L63))
   - Currently: each root has its own `CombatTargetRegistry<ICombatTarget>`
   - New: unified root needs to serve both player and mob targets (or separate registries per faction)

4. **Spatial hash faction keying** (ProjectileCollisionSystem, AoeCollisionCore, ProjectileTrackingSystem)
   - **Already faction-aware** — no structural changes needed; can remain as-is
   - Optimization: could use granular faction+spatial_region keys later (deferred)

5. **Render resource registry** ([Assets/Scripts/System/Common/CombatRoot.cs:58-61](../../Assets/Scripts/System/Common/CombatRoot.cs#L58-L61))
   - Currently: per-root render resource dicts and `renderResourcesById`
   - Consolidated root will need: faction-aware render ID mapping or shared render space

## Documentation Gaps

- **CombatRoot unity and federation**: How does a unified root serve two factions? Does it have two target registries, or one? How does `CanTarget(ICombatTarget)` work across factions?
- **Spawn request to faction mapping**: Who provides faction when a spawn request is submitted to the unified root? (Currently: each root is faction-aware, spawn request doesn't carry faction)

## Recommended Next Step

**Documentation is sufficient for planning.** The architecture is already halfway there (shared world, shared scope, faction as field). The consolidation is primarily a refactoring of the managed boundary (scene setup, root instance count, target registry ownership) — the ECS simulation systems are already faction-aware and require no structural changes. Plan should focus on:

1. Unified CombatRoot class design (single instance vs. two target registries?)
2. Scene/GameRoot wiring changes
3. Spawn request faction handling
4. Test coverage for cross-faction scenarios

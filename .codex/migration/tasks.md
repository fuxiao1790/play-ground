# Godot-To-Unity Migration Tasks

1. Rebuild full main scene composition.
   Unity `Main.unity` should match old `_OldGdProj/Scenes/level/main.tscn`: player, camera, play area wall, debug overlay, mob spawner, spawn points, player projectile root, mob projectile root, player AOE root, and mob AOE root.

2. Finish player parity.
   Add dash, acceleration/friction tuning parity, animation driver, health/hurt handling, max attack count, and multiple equipped attack children like old `player.tscn`.

3. Port real mobs.
   Replace `TargetDummyRoot` scaffolding with bat/slime/skeleton roots, health, hurtbox, animation, local event queue, behavior FSM, triggers, behaviours, debuff stacks, soft death, and mob projectile attacks.

4. Port spawn system. DONE
   Added `MobSpawnerRoot`, `SpawnPoint`, spawn config/pools, scene-authored spawn points, cap rules, and mob prefab selection from the old spawn setup.
   Current `Main.unity` has a separate editable `MobSpawnerRoot` with four child spawn points. The spawner owns fallback runtime mob prefab creation only until authored mob prefabs/pools replace it.

5. Fix projectile rendering.
   Projectile physics and damage work; visible projectile rendering is broken. Focus only on `Assets/Scripts/System/Projectile/ProjectileRoot.cs` render path: sprite/material/shader setup, mesh UVs, `Graphics.DrawMeshInstanced`, camera/layer visibility, sorting/render queue, and ECS projectile query feeding render matrices. Do not change collision or damage while fixing this.

6. Expand projectile runtime. DONE
   Added scoped ECS buffers for hits and child spawn requests, baked circle/box/capsule target snapshots, target masks, tracking/reacquire, pierce repeat-hit gates, ordered hit replay, damage snapshot forwarding, runtime counters, and render batching by projectile type.

7. Port attack authoring. DONE
   Add old attack fields and behavior: sound, projectile templates, tracking config, pierce, impact AOE, direct-damage toggle, child hit effects, stack explosion effects, travel-spawn patterns, and side-spray patterns.
   Added Unity projectile attack authoring fields, child spawn pattern assets, side-spray default behavior, hit-effect hooks, stack explosion effect requests, and direct-damage gating. Impact/stack AOE now emits adapter requests; actual AOE root consumption lands with task 8.

8. Port AOE system.
   Add `Assets/Scripts/System/Aoe`: `AoeRoot`, `AoeWorld`, type registry, target sync, pulse AOEs, lingering tick AOEs, exit/re-entry gates, projectile impact AOE, stack-triggered AOE, and optional visual pooling.

9. Restore camera parity.
   Match old oval dead-zone, mouse bias, zoom, wall-sized release limits, and expanded debug-build limits.

10. Restore play area parity.
    Add configurable 1280x720 default wall with proper Unity Physics2D colliders/layers, matching old player/mob/environment body collision.

11. Port audio manager.
    Add pooled one-shot audio playback with same-clip simultaneous culling.

12. Set up gameplay layers and collision matrix. DONE
    Add named Unity layers: `PlayerBody`, `PlayerHurtbox`, `PlayerProjectile`, `PlayerAoe`, `MobBody`, `MobHurtbox`, `MobProjectile`, `MobAoe`, and `Environment`.
    Gameplay layers now exist in `TagManager.asset`. Physics2D contacts are limited so player bodies collide with mob bodies and environment, mob bodies collide with player bodies, other mob bodies, and environment, and hurtbox/projectile/AOE layers are reserved for explicit runtime masks/snapshots.

13. Replace bare-minimum prototype content.
    Replace current dummy/prototype prefabs with real prefabs for player, bat, slime, skeleton, attacks, projectile templates, AOE effects, and play area.

14. Port tests.
    Add Unity EditMode/PlayMode coverage matching old Godot smoke tests: player movement/dash, camera, wall, spawn, mob FSM, projectile tracking/pierce/render, AOE pulse/linger, impact AOE, stack explosions, audio, main-scene smoke, and stress scene.

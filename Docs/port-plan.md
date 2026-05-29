# Port Plan

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Goal

Build a top-down action game in Unity with extreme attack scaling.

Performance is part of the game identity. Future builds should support huge
projectile storms, lasers, AOEs, and particle-heavy chains.

Hybrid split:

- player, mobs, walls, spawners, camera, audio, and debug are scene objects
- target count is expected to be far below projectile count; player plus mobs
  below roughly `50` is an early performance target
- Unity Physics2D handles player/mob/wall movement and collision
- projectiles, AOEs, and beams use data-oriented runtimes

## Docs

- `Docs/project-overview.md`
- `Docs/folder-structure.md`
- `Docs/architecture.md`
- `Docs/coding-standards.md`
- `Docs/gameplay.md`
- `Docs/player-attacks.md`
- `Docs/projectile-system.md`
- `Docs/aoe-system.md`
- `Docs/mob-behaviour.md`
- `Docs/mobs.md`
- `Docs/testing.md`
- `Docs/release.md`

## Milestones

1. Docs port complete.
2. Folder structure created.
3. Shared runtime primitives: damage snapshot, state machine core, target handles.
4. Player movement, dash, facing, and attack loadout.
5. Spawn root and one mob prefab.
6. Mob behavior FSM, soft death, and mob projectile path.
7. Projectile runtime with scoped roots, split ECS stages, baked shapes, tracking,
   pierce/contact gates, child spawn requests, counters, and batched rendering.
8. Camera and play area parity.
9. AOE runtime and direct player AOE.
10. Projectile impact AOE and stack-triggered explosion.
11. Audio manager with duplicate culling.
12. Debug overlay with performance counters.
13. PlayMode, EditMode, and stress test coverage.
14. Beam/laser runtime skeleton when beam gameplay becomes active work.
15. Windows build.

## Development Rules

- Use Unity-native prefab, scene, ScriptableObject, Physics2D, and Test Framework patterns.
- Keep root components as coordinators.
- Keep high-volume simulation plain-data and Jobs/Burst-friendly.
- Keep actor movement/collision on Physics2D unless profiling or gameplay proves
  it cannot satisfy the low-count actor requirement.
- Add stress scenes early.
- Use Jobs/Burst and batched projectile rendering to validate the intended scale.

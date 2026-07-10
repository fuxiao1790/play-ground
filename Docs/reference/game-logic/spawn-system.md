# Spawn System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Current Shape

The previous mob spawning implementation has been removed. There is currently no
runtime `Assets/Scripts/Spawn/` implementation, no spawn-point scene objects, and
no spawn-pool ScriptableObject contract.

Mobs themselves remain authored under `Assets/Scripts/Mob/` and
`Assets/Prefabs/Mobs/`.

## Runtime Rules

- Replacement spawning rules are not yet defined.
- Future spawn code should continue to create low-count Unity `MobRoot` actors,
  then bind them to combat target registration through the existing mob root
  path.

## Temporary Fallback

The runtime fallback mob prefab path has been deleted with the old spawning
system. Use real mob prefabs when the replacement spawner is introduced.

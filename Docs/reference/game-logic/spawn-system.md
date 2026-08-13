# Spawn System

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Current Shape

Runtime spawning lives in `Assets/Scripts/Spawn/`:

- `SpawnController` owns rate behavior, cap checks, placement, combat binding,
  pending confirmations, and pool reclaim.
- `SpawnPoint` supplies authored scene positions and samples disabled collider
  shapes.
- `MobSpawnTable` is the ScriptableObject prefab-selection contract.
- `MobPool` rents disabled mob instances and receives reclaimed instances.

Mobs remain authored under `Assets/Scripts/Mob/` and `Assets/Prefabs/Mobs/`.

## Runtime Rules

- Spawn decision, weighted prefab knowledge, placement, and pooling stay in
  GameObject code.
- A rent is disabled until its ECS proxy returns through the presentation spawn
  bridge; pending rents count toward the cap.
- ECS decides tagged-proxy death. The actor hides and pool reclaim happen on the
  next `Update()` after the presentation despawn push.
- Proxy entities create and destroy once per mob life. They are not pooled with
  projectile/AOE combat entities.

## Runtime Fallback

`MobPool` cold-instantiates the selected authored prefab when its matching free
stack is empty. Prewarm provides the usual allocation-free spawn path.

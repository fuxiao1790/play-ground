# 002 — Projectile detonation spawn in StackAccrualSystem

## Change kind: adapt

## Structural role
Implements the missing `Projectile` case so threshold detonation fires a nova, reusing the
existing AOE→projectile burst builder. Closes the reachable silent no-op.

## Ownership / data flow
- [BuildDetonationSpawn](Assets/Scripts/System/Aoe/StackAccrualSystem.cs#L239) gains:
  ```
  case StackDetonationKind.Projectile:
      projectileExpansion.EventQueue.Enqueue(BuildProjectileDetonation(entry, position));
      return;
  default:
      // unreachable kind — assert/log, never silent (focus item 7)
  ```
- `BuildProjectileDetonation` takes the entry's `DetonationSnapshot.ProjectileBurst`, overwrites
  `Count = entry.SummedProjectileCount` and `Damage = entry.SummedDamage` (the contribution),
  and calls [ProjectileSpawnPipeline.BuildBurstEvent](Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs#L115)
  at `position`. Same builder AOE bursts already use.
- Wire `ProjectileSpawnExpansionSystem` into the system: cache it like the existing
  `AoeSpawnExpansionSystem`, enqueue into its `EventQueue`, and combine its `ProducerHandle`
  with this system's write (mirror the AOE wiring already present).

## Phase/order
- Add `[UpdateBefore(typeof(ProjectileSpawnExpansionSystem))]` next to the existing
  `[UpdateBefore(AoeSpawnExpansionSystem)]` so the nova event is consumed the same frame.
- `StackAccrualSystem` remains the single writer of stack state; the projectile queue is an
  output channel only.

## Structural notes
- Damage rule: `SummedDamage` is the nova **total**; `BuildBurstEvent`'s per-projectile damage =
  `SummedDamage / Count` (document the rule; mark exact split a TODO if design wants per-stack).
- The `default` branch makes the switch total over reachable kinds — no fallthrough no-op.

## Acceptance criteria
- A projectile-detonation entry at threshold enqueues a `ProjectileSpawnEvent` and the nova
  materializes through the normal projectile apply path.
- `SummedProjectileCount` projectiles fire; total damage = `SummedDamage`.
- AOE detonation path unchanged; an unhandled kind logs/asserts.

## Dependencies
001 (atomic).

## Scope
Medium.

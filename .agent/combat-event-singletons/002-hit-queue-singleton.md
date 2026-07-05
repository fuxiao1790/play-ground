# 002 — HitQueue Singleton

## Goal

Relocate `CombatApplyFinalizeSingleSystem`'s `NativeQueue<CombatHitEvent> HitQueue` and its
`JobHandle ProducerHandle` into `CombatHitDispatchSingleton`. Migrate the three collision
producers off `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>()`.

Depends on: 001 (uses the conventions it established).

## Scope / files

**Sink** — [CombatApplyFinalizeSingleSystem.cs](../../Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs):
- Add:
  ```csharp
  // ECS Lifecycle: singleton combat-hit dispatch queue; created by CombatApplyFinalizeSingleSystem
  // on create, drained every simulation update by the finalize job, disposed on destroy.
  public struct CombatHitDispatchSingleton : IComponentData
  {
      public NativeQueue<CombatHitEvent> HitQueue;
      public JobHandle ProducerHandle;
  }
  ```
- `OnCreate`: create `HitQueue`, create singleton entity holding it.
- `OnDestroy`: `ProducerHandle.Complete()` → dispose queue (unchanged order).
- `OnUpdate`: read `HitQueue` from the singleton; the `FinalizeCombatSingleJob` still takes
  the queue by value and dequeues it — unchanged. `ProducerHandle.Complete()` at the top of
  `OnUpdate` now reads/writes the singleton copy.
- Remove `internal NativeQueue<CombatHitEvent> HitQueue`, `internal JobHandle ProducerHandle`,
  `internal ... AsParallelWriter()`.
- Note: `AccrualFrame` / `LastHitEventCount` stay as system state (not shared) — do **not**
  move them into the singleton.

**Producers** — the three collision systems (`HitWriter` / `HasHitWriter` / `hitApply.ProducerHandle`):
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs)
- [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs)
- [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)

Swap to `TryGetSingletonRW<CombatHitDispatchSingleton>`; job field
`HitWriter = has ? rw.ValueRO.HitQueue.AsParallelWriter() : default`,
`HasHitWriter = has`; post-schedule combine into `rw.ValueRW.ProducerHandle`.

## Acceptance criteria

- No `GetExistingSystemManaged<CombatApplyFinalizeSingleSystem>()` remains.
- Sink exposes no `internal` queue/handle/writer members.
- Damage + status still apply (manual play: mobs take damage, stacks accrue).
- All PlayMode tests green — especially `CombatPoolCleanupSystemTests`,
  `ProjectileCollisionSimulationTests`, and damage/stack assertions in `AoeSimulationTests`.
- Jobs Debugger + Leak Detection clean.

## Scope estimate

Small–medium. One sink + three near-identical collision edits.

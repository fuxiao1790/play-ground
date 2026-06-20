# 004 — ECS stack accrual

## Structural role
The core of the chosen design: move stack accumulation and the next-stage spawn
into ECS so the stack trigger is handled like the projectile burst follow-up —
collision emits intent, an aggregation system owns state and emits a spawn event.
No managed round-trip.

## Ownership / data flow
```
AOE collision job  (sim)        -> StackApplyEvent { TargetProxy, Chain, Faction, Mask }
StackAccrualSystem (aggregation) -> mutate TargetStackStateComponent (SOLE writer)
                                  -> on threshold: AoeSpawnEvent { Chain[0].Aoe, chain = Chain[1..] }
AoeSpawnExpansionSystem/Apply    -> materialize spawned AOE carrying the tail
```

## New types
- `TargetStackStateComponent : IComponentData` — per-status counts for the target
  proxy (fixed inline, sized to the `MobDebuffStatus` enum count). Added to the
  proxy archetype in `CombatTargetProxy.Archetype`.
- `StackApplyEvent` — `{ Entity TargetProxy; CombatFaction Faction; int TargetMask;
  FixedList…<StackStage> Chain; }`. Position read from the proxy at accrual time.
- `StackAccrualSystem : SystemBase` — owns a `NativeQueue<StackApplyEvent>` and
  exposes a `ParallelWriter`; mirrors `DamageDispatchBridge`'s producer-handle +
  drain pattern.

## Change
- Define the three types above; add `TargetStackStateComponent` to the proxy
  archetype (and reset it on proxy create).
- `AoeCollisionCore.RunCollision`/`EmitHit`: add a `StackApply` parallel writer
  param; when `Chain.Length > 0`, enqueue a `StackApplyEvent` carrying
  `Chain` (element `[0]` is this AOE's trigger), `identity.Faction`, and the mask
  from the carrier. (Parallel to the existing `projectileEventWriter` burst emit.)
- `LingeringAoeCollisionSystem` + `ImpactAoeCollisionSystem`: fetch
  `StackAccrualSystem`, wire its writer into the job, and combine its
  `ProducerHandle` with the collision handle (exactly like `DamageDispatchBridge`).
- `StackAccrualSystem.OnUpdate`: complete producers; drain the queue grouped by
  `(TargetProxy, DebuffStatusId)`; add `StacksPerHit` to
  `TargetStackStateComponent`; when `count >= StackThreshold`, emit an
  `AoeSpawnEvent` (origin = proxy `TargetPosition`, type/damage/lifetime/geometry
  from `Chain[0]`, faction/mask from the event, **chain = `Chain[1..]`**) into
  `AoeSpawnExpansionSystem.EventQueue`, then subtract the threshold (carry-over
  remainder — leave the reset-vs-carry rule as a documented TODO).
- Order: `[UpdateInGroup(SimulationSystemGroup)]`,
  `[UpdateAfter(LingeringAoeCollisionSystem)]`,
  `[UpdateAfter(ImpactAoeCollisionSystem)]`,
  `[UpdateBefore(AoeSpawnExpansionSystem)]`.

## Structural notes
- **Single writer**: only `StackAccrualSystem` writes `TargetStackStateComponent`.
  Collision is read-free of stack state. Document this on the component.
- Stale targets: one `EntityManager.Exists`/`HasComponent` guard before applying;
  drop the event if the proxy is gone. No other defensive code.
- Stacks now have their own phase path end-to-end; nothing in the damage path
  references them.

## Acceptance criteria
- `LingerA` hitting a mob accrues stacks in ECS; at threshold an `AoeSpawnEvent`
  for `LingerB` is emitted carrying `[Impact]` as its chain — no `MobRoot` call.
- `LingerB` then accrues and at threshold spawns `Impact` (empty chain, terminal).
- Two distinct statuses on one mob accrue independently.
- Collision jobs do not read or write target stack state.

## Dependencies
003 (chain present on the AOE entity and in `AoeSpawnEvent`).

## Scope
Large. New ECS state + aggregation system on the hot collision path; highest risk.

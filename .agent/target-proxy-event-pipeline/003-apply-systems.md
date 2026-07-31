# 003 — Apply Systems

## Change

Three new files in `Assets/Scripts/System/Targets/`:

**`TargetProxyCreateApplySystem.cs`** — `partial class : SystemBase` (non-Burst: touches
the managed `pendingCreates` dictionary and the managed `TargetCompanion` component).
`OnUpdate`: for each scope entity's `TargetProxyCreateEvent` buffer, for each event —
if `CombatTargetProxy.IsPendingCreateCancelled(event.Token)` (or equivalent), skip; else
`TryTakePendingCreate(event.Token, out ICombatTarget target)`, create the entity via
`CombatTargetProxy`'s existing cached archetype (`TargetProxyTag`, `TargetPosition`,
`TargetCollisionShape`, `TargetFaction`, `Health`, `Mana`, `TargetStackEntry`,
`TargetCompanion`), `SetComponentData` for Faction/Position/Shape/Health/Mana from the
event fields, `SetComponentData(entity, new TargetCompanion { Target = target })`,
`target.CombatTargetProxy = entity`. Clear the buffer after processing.

**`TargetProxyUpdateApplySystem.cs`** — `partial class : SystemBase`. `OnUpdate`: for
each scope entity's `TargetProxyUpdateEvent` buffer, for each event in order — if
`!entityManager.Exists(event.Proxy)`, skip (target may have been deleted same-frame);
else switch on `Kind`:
- `Push` → `SetComponentData(event.Proxy, event.Position)`, `SetComponentData(event.Proxy, event.Shape)`.
- `PushResourceMaxes` → read `Health`/`Mana`, update `Max`/`RegenPerSecond`, clamp
  `Current` to new `Max` (reproduces `PushResourceMaxes(EntityManager, Entity, ICombatTarget)`
  logic, `CombatTargetProxy.cs:226-248`), write back.
- `SetHealth`/`SetMana` → read `Health`/`Mana`, set `Current = clamp(event.CurrentValue, 0, Max)`, write back.
Clear the buffer after processing.

**`TargetProxyDeleteApplySystem.cs`** — `partial class : SystemBase`. `OnUpdate`: for
each scope entity's `TargetProxyDeleteEvent` buffer, for each event — if
`entityManager.Exists(event.Proxy)`, `DestroyEntity(event.Proxy)`. Clear the buffer
after processing.

All three iterate every `CombatScope`-tagged entity (same multi-scope-tolerant query
shape already used by `ProjectileSpawnExpansionSystem`), not just a single hardcoded
scope.

## System Ordering (explicit attributes)

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TargetProxyUpdateApplySystem))]
[UpdateBefore(typeof(TargetSpatialHashSystem))]
public partial class TargetProxyCreateApplySystem : SystemBase

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TargetSpatialHashSystem))]
public partial class TargetProxyUpdateApplySystem : SystemBase

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(CombatApplyBridge))]
public partial class TargetProxyDeleteApplySystem : SystemBase
```

**Why `UpdateBefore(TargetSpatialHashSystem)` is sufficient for both early systems**:
confirmed via `Broadphase/TargetSpatialHashSystem.cs:42-46` — it already declares
`[UpdateBefore(typeof(ProjectileTrackingSystem))]`, `[UpdateBefore(typeof(ProjectileCollisionSystem))]`,
`[UpdateBefore(typeof(LingeringAoeCollisionSystem))]`, `[UpdateBefore(typeof(ImpactAoeCollisionSystem))]`,
which themselves chain into `CombatApplyFinalizeSingleSystem` and `ResourceRegenSystem`.
One constraint transitively preserves "proxy data is current before every consumer
reads it" (`Docs/flows/runtime-frame.md` step 2) without needing to enumerate every
downstream system by hand.

**Why Delete must be `PresentationSystemGroup, UpdateAfter(CombatApplyBridge)`
specifically**: confirmed via `CombatApplyBridge.cs:10,91` —
`CombatApplyBridge.ReplayCombat` is what reads `TargetCompanion` off
`result.TargetProxy` to fire `ReceiveHit`/`ReceiveCombatTick` for the frame, and it runs
in `PresentationSystemGroup`. Destroying the entity any earlier drops that frame's final
hit/death notification for the target. `SimulationSystemGroup, UpdateAfter(CombatApplyFinalizeSingleSystem)`
is *not* sufficient — `CombatApplyFinalizeSingleSystem` only writes a compact
`NativeList<CombatTickResult>` singleton; the actual `TargetCompanion` read happens
later, in Presentation.

## Explicitly Not Changing

`PlayerRoot.cs`/`MobRoot.cs`'s `deleteProxyInLateUpdate`/`LateUpdate()` deferral stays
exactly as-is — only what happens inside it changes (calls `CombatTargetProxy.Delete`,
which now enqueues instead of destroying directly). Both `runtime-frame.md:69` and
`target-proxy-lifecycle.md:61` carry an open `TODO: verify actor LateUpdate() ordering
against ECS presentation systems`; this plan does not resolve that TODO or build new
assumptions on top of it.

## Acceptance Criteria

- A `TargetProxyCreateEvent` enqueued in frame N produces a valid, queryable entity
  with `TargetCompanion` correctly pointing back at the managed target, and
  `target.CombatTargetProxy` correctly set, by the end of the next `World.Update()`.
- A cancelled pending create (target deleted before its create-apply tick) produces no
  entity.
- Events enqueued for a `Proxy` that no longer exists (deleted same-frame) are skipped,
  not thrown.
- `TargetProxyDeleteApplySystem` runs after `CombatApplyBridge` in the same frame's
  `PresentationSystemGroup` pass — verify via a test that enqueues a delete and a hit
  result in the same frame and asserts `ReceiveHit` still fires before the entity is
  gone.

## Dependencies

Depends on 001 (buffers) and 002 (`CombatTargetProxy`'s pending-create accessors).

## Scope

Medium. Three small-to-medium systems (~40-80 lines each); the ordering attributes are
the highest-risk part and are already traced against concrete existing system
declarations above, not left to trial and error.

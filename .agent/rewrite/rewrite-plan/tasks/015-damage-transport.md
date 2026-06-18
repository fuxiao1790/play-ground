# Task 015: Entity-keyed damage + native-container transport

## Goal
Realize design §8.1 (D-DAMAGE-TRANSPORT): damage replay is keyed by the target proxy `Entity` and flows through `NativeQueue<DamageReplayEvent> → NativeArray → DamageDispatchBridge`. Delete the `CombatHitFlushJob` flush stage and the scope `CombatDamageElement` buffer. Dispatch resolves `Entity → TargetCompanion → ICombatTarget`.

This reverses the prior plan's "keep the buffer + flush job, rename only" (old Task 008) and its `(TargetId, Faction)` identity.

## Required Reading
- `.agent/rewrite/design.md` §8.1, §8.4, §6
- `../context/002-target-architecture.md` §1.5, §2 (Damage dispatch), §3 (container table)
- `../context/005-decision-log.md` → D-DAMAGE-TRANSPORT, D-PROXY-ENTITY
- `../context/003-data-flow.md` §5; `../context/004-system-ordering.md` (9.7 finalize)

## New type (`System/Common/CombatHitElement.cs`)
```csharp
public struct DamageReplayEvent     // blittable
{
    public Entity        TargetProxy;   // identity (replaces TargetId + Faction)
    public float2        HitPosition;
    public float2        HitDirection;
    public CombatHitKind Kind;
    public float         DamageAmount;
    public float         CritChance, CritMultiplier;   // rolled on the main thread in the bridge
    public bool          DirectDamageEnabled;
    public EntityId      SourceNodeId;
    public CombatStatusEffectSnapshot StackEffect;
    public int           SourceId, TypeId;             // carried if dispatch still needs them
}
```

## Current Code References
- `System/Common/CombatHitFlushJob.cs` — `IJob` copying the damage `NativeStream` into the `CombatDamageElement` buffer. **Deleted.**
- `System/Common/CombatHitElement.cs` — `CombatDamageElement` buffer element. **Deleted** (struct + scope add). `CombatPendingDamage`/`DamageReplayEvent` reshaped per above.
- `System/Common/DamageDispatchBridge.cs` — reads the `CombatDamageElement` buffer, groups by `(TargetId, Faction)`, resolves via `CombatRoot.TryGetByFaction(...).TargetsById`. **Repointed** to the `NativeArray` + proxy/companion.
- `System/Projectile/ProjectileCollisionSystem.cs`, `System/Aoe/AoeCollisionSystem.cs` — write damage; now enqueue `DamageReplayEvent{ TargetProxy = hit proxy entity }` into the damage `NativeQueue.ParallelWriter`.
- `System/Projectile/ProjectileSimulationSystem.cs`, `System/Aoe/AoeSimulationSystem.cs` — the `CombatDamageElement` OrderFirst clears. **Removed** (no buffer; D-DAMAGE-CLEAR obsolete). If `AoeSimulationSystem` becomes empty, delete it.

## Required Changes
1. **Damage queue:** `DamageDispatchBridge` owns a persistent `NativeQueue<DamageReplayEvent>` (DECIDED — not NativeStream; §8.1). Created in `OnCreate`, disposed in `OnDestroy`; owner asserts it is empty before producers write each frame. Collisions get `.AsParallelWriter()` via `World.GetExistingSystemManaged<DamageDispatchBridge>()`.
2. **Collision writes Entity-keyed events** into the queue's `ParallelWriter` (relocate from the current `NativeStream` damage write). `TargetProxy` = the proxy entity the spatial query matched (Task 014).
3. **Finalize (phase 9.7):** after both collisions complete, freeze the queue → `NativeArray<DamageReplayEvent>`.
4. **Bridge:** `DamageDispatchBridge` reads the array, groups by `TargetProxy`, rolls crit on the main thread (`UnityEngine.Random` — unchanged), resolves the managed target via `EntityManager.GetComponentObject<TargetCompanion>(TargetProxy).Target` (managed class component → `GetComponentObject`, not `GetComponentData`), calls `ReceiveHits`. It is the only `TargetCompanion` reader (§8.4). Then `Clear()` the queue for the frame.
5. **Delete** `CombatHitFlushJob`, `CombatDamageElement`, the OrderFirst clears, and the scope buffer add.
6. Recompile; run `CritEditModeTests` + the suite.

## Behavior Preservation Requirements
- Atomic per-hit (no condensation), group-by-target (now by `Entity`), one `ReceiveHits` per group, crit roll on the main thread — all unchanged in logic.
- The queue's empty-before-write / clear-after-read contract (§6) replaces the OrderFirst double-clear; net damage applied per frame is identical.

## Dependencies
014 (proxy `Entity` + `TargetCompanion` exist). Pairs with 013 in collision edits — sequence after 014 to avoid editing collision twice.

## Acceptance Criteria
- [ ] `DamageReplayEvent.TargetProxy : Entity`; no `TargetId`/`Faction` identity.
- [ ] Damage flows `NativeQueue → NativeArray → bridge`; `CombatHitFlushJob` + `CombatDamageElement` deleted.
- [ ] Bridge resolves via `TargetCompanion`; it is the sole companion reader.
- [ ] No `CombatDamageElement` clear systems remain; queue cleared after dispatch.
- [ ] `CritEditModeTests` pass; repo compiles; suite green.

## Risk
High — identity + transport reshape on the hot damage path. Mitigate: Entity-keyed damage test, proxy-deletion-safety test, crit parity (Task 017); profile for the queue→array sync point.

## Failure Modes
- **No/NRE damage:** `TargetProxy` deleted before dispatch (defer deletion, Task 016) or companion lookup missed.
- **Double/lost damage:** queue read while collisions still write (missing finalize sync), or not cleared after dispatch.

# Task 014: Proxy-entity target bridge + managed companion

## Goal
Realize design §8.2/8.3 (D-PROXY-ENTITY): every `ICombatTarget` GameObject owns an ECS **proxy entity** carrying its unmanaged simulation state plus a **managed companion** back-reference. Collision reads proxy entities. Retire the `CombatTargetElement` buffer sync.

This replaces the `(TargetId + Faction)` + `CombatTargetSync`/`CombatTargetSyncSystem` model. The §8-vs-§11 contradiction is resolved in favor of §8 (see index Open Questions).

## Required Reading
- `.agent/rewrite/design.md` §8.2, §8.3, §8.4, §9
- `../context/002-target-architecture.md` §1.6, §2 (Target bridge)
- `../context/005-decision-log.md` → D-PROXY-ENTITY
- `../context/003-data-flow.md` §5a; `../context/004-system-ordering.md` (frame boundary)

## New ECS types (`System/Common/`)
```
public struct TargetProxyTag : IComponentData {}
public struct TargetPosition { public float2 Value; }
public struct TargetCollisionShape { public CombatShapeType ShapeType; public float Radius;
    public float2 HalfExtents; public float RotationRadians; public float2 BoundsMin, BoundsMax; public int Mask; }
public struct TargetFaction { public CombatFaction Value; }
public sealed class TargetCompanion : IComponentData { public ICombatTarget Target; }  // managed; read ONLY by DamageDispatchBridge
```
DECIDED: there is **no** alive/enableable flag on the proxy — proxy existence == targetable (§8.3). Leaving simulation deletes the proxy. The companion is a **managed class `IComponentData`** (above), not a struct.

## Proxy creation helper (`System/Common/CombatTargetProxy.cs`)
A static helper that builds the proxy archetype **once** (cached) and exposes `Create(EntityManager, ICombatTarget, CombatFaction) → Entity` and `Delete(EntityManager, Entity)`. The `ICombatTarget` MonoBehaviour calls these; the archetype is not rebuilt per target.

## Current Code References
- `System/Common/CombatTargetSync.cs` — `SyncToBuffer` fills `CombatTargetElement`; builds bounds via `CombatCollisionMath.ComputeWorldBounds`; maintains `targetsById`. **Logic to relocate:** the bounds build + active filter.
- `System/Common/CombatTargetSyncSystem.cs` — OrderFirst clear+fill. **Removed** (proxies replace it).
- `System/Common/CombatTargetRegistry.cs` — keep (managed registry of live targets).
- `System/Common/ICombatTarget.cs` — gains proxy-lifecycle surface.
- `System/Common/CombatRoot.cs` — `AppendTargetsToBuffer` removed. Proxy creation/deletion goes through the `CombatTargetProxy` helper (above), called by the target MonoBehaviour — NOT owned by `CombatRoot`.
- Collision systems read `CombatTargetElement` for the spatial hash — **repointed** to proxy entities.

## Required Changes
1. **Proxy creation:** the `ICombatTarget` MonoBehaviour calls `CombatTargetProxy.Create(...)` on enable (sets `TargetProxyTag`, `TargetPosition`, `TargetCollisionShape`, `TargetFaction`, `TargetCompanion{ this }`) and stores the returned `Entity` handle.
2. **Position push (`Update()`):** each `Update()`, **before** the ECS world ticks, the GameObject writes its current `TargetPosition` + `TargetCollisionShape` (reuse `ComputeWorldBounds`) into its proxy. Relocate the bounds/shape build from `CombatTargetSync` verbatim.
3. **Deletion (`LateUpdate()`, §8.3):** when the target stops simulating, the MonoBehaviour calls `CombatTargetProxy.Delete(...)` in `LateUpdate()` (after damage dispatch has run in `Update`). Immediate delete — not disabled, not pooled. Death animation may continue on the GameObject afterward.
4. **Collision source swap:** `ProjectileCollisionSystem`/`AoeCollisionSystem` build their per-faction spatial hash from a query over `(TargetProxyTag, TargetPosition, TargetCollisionShape, TargetFaction)` instead of the `CombatTargetElement` buffer. Keep the hash cell sizes (projectile 1, AoE 64) and the math unchanged. Collision now records the **hit proxy `Entity`** (needed by Task 015).
5. **Remove** `CombatTargetSyncSystem` + `CombatTargetSync.SyncToBuffer` + the `CombatTargetElement` buffer add in `CombatScopeOwner` (if nothing else reads it after Task 015).
6. Recompile; run.

## Behavior Preservation Requirements
- Spatial query results identical to the buffer-based hash (same targets, same bounds; the "active" filter is now simply "a proxy entity exists" — there is no alive flag, §8.3).
- `ComputeWorldBounds` and collision math untouched (Non-Goal).
- Only `DamageDispatchBridge` may read `TargetCompanion` (Global Invariant 5 / §8.4); no Burst/collision system dereferences it.

## Dependencies
011 (collision/queries use `Active`). Feeds 015 (damage needs the proxy `Entity`).

## Acceptance Criteria
- [ ] Each live target has exactly one proxy entity; created on enable, deleted on leaving sim.
- [ ] GameObject pushes position each `Update()` before simulation; collision reads proxies.
- [ ] `CombatTargetSyncSystem` + `CombatTargetElement` sync removed (buffer gone once Task 015 lands).
- [ ] `TargetCompanion` is managed and untouched by any job.
- [ ] Repo compiles; suite green (tests create proxies for targets).

## Risk
High — target-bridge rebuild + collision source swap + GameObject↔ECS lifecycle. Mitigate: land behind the existing collision math; proxy-lifecycle + spatial-parity tests (Task 017); verify no managed access in jobs (Burst will error if violated).

## Failure Modes
- **Wrong/stale hits:** position not pushed before simulation (ordering, Task 016).
- **NRE on dispatch:** proxy deleted before dispatch — defer deletion to after dispatch (§8.3).
- **Burst error:** a job touched `TargetCompanion`.

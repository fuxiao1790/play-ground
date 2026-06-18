# Context 004 — Final System Ordering & Phase Rules

The order below is the **contract** (design §9). Local `[UpdateBefore]`/`[UpdateAfter]` attributes remain useful but must not contradict this. Every task that adds, renames, or moves a system must keep this order intact.

---

## MonoBehaviour frame boundary (design §9)

```
Update()      — live ICombatTarget GameObjects push TargetPosition/TargetCollisionShape into
                their proxy entity BEFORE ECS simulation; targets leaving simulation delete
                their proxy entity here (so it is gone before collision reads proxies).
ECS Simulation — phases below.
Damage Dispatch — DamageDispatchBridge (PresentationSystemGroup), same Update frame.
LateUpdate()  — death animation / scene cleanup / GameObject destruction; no unresolved
                damage event references a deleted proxy.
```

## SimulationSystemGroup

`OrderFirst = true`:
- (no damage-buffer clear anymore) — the `CombatDamageElement` buffer is removed (D-DAMAGE-TRANSPORT). The damage `NativeQueue` is asserted-empty by its owner before producers write, and `Clear()`'d after the bridge reads it (§6 phase discipline), so there is no frame-start clear system to own.

Ordered simulation phases (design §9.1–9.10):

| Phase | System(s) | Notes |
|---|---|---|
| 9.1 Lifetime / active | `CombatLifetimeSystem` | unified; over `(Active, enabled CombatLifetimeComponent)`; before any collision so expired entities don't collide |
| 9.2 Timed spawn production | `TimedProjectileSpawnSystem` | producer → `ProjectileSpawnEvent` queue |
| 9.3 Tracking / movement | `ProjectileTrackingSystem`, `ProjectileMovementSystem` | unchanged |
| 9.4 Projectile contact gate | `ProjectileContactGateSystem` | gate-state maintenance only |
| 9.4 Projectile collision | `ProjectileCollisionSystem` | gate inline; reads proxy entities; emits typed consequence events incl. `Entity`-keyed `DamageReplayEvent` |
| 9.5 AoE contact gate | `AoeContactGateSystem` | |
| 9.5 AoE collision | `AoeCollisionSystem` | gate inline; reads proxy entities; emits typed events |
| 9.7 Damage finalize | damage-queue owner | freeze `NativeQueue<DamageReplayEvent>` → `NativeArray` after both collisions complete |
| 9.8 Spawn expansion | `ProjectileSpawnExpansionSystem`, `AoeSpawnExpansionSystem` | events → commands; all math; routes to per-shape command containers; AFTER all collision |
| 9.9 Spawn apply | per-shape: `BasicProjectileSpawnApplySystem`, `ChildSpawnerProjectileSpawnApplySystem`, `AoeSpawnApplySystem` | reuse (`WithDisabled<Active>`) / cold-create; LAST simulation write of new entities |
| (pulse vfx) | `AoePulseVfxSystem` | after lifetime; before render prepare |
| (render) | `CombatRenderPrepareSystem` | unchanged; after apply |

## PresentationSystemGroup
- `DamageDispatchBridge` — reads the finalized `NativeArray<DamageReplayEvent>`, groups by `TargetProxy`, resolves `Entity → TargetCompanion → ICombatTarget`, replays; clears the damage queue for the frame.
- `CombatBatchedRenderSystem`, `CombatVfxDispatchSystem` — unchanged.

---

## Required ordering invariants

1. **Next-tick spawn (R5):** every phase that could process a *new* entity (9.1 lifetime, 9.2 timed-spawn, 9.3 tracking/movement, 9.4/9.5 collision) MUST run before spawn apply (9.9). Enforced **by order only** — no `SpawnedThisTick` marker.
2. **Proxy state before collision:** GameObjects push proxy position in `Update()` and delete leaving proxies in `Update()`/`LateUpdate()` such that collision (9.4/9.5) reads only valid, current proxies.
3. **Producers before expansion:** `TimedProjectileSpawnSystem` and both collisions MUST run before `ProjectileSpawnExpansionSystem`; both collisions before `AoeSpawnExpansionSystem`.
4. **Damage finalize after collisions, before dispatch:** the `NativeQueue → NativeArray` freeze (9.7) runs after both collisions and before the presentation-group bridge reads it.
5. **Expansion before apply;** **both collisions before both expansions** (cross-domain impact consequences).
6. **Lifetime before collision** so a just-expired entity is excluded the same frame.

## Read/write permissions by data type

| Data | May write | May read | Forbidden |
|---|---|---|---|
| proxy `TargetPosition`/`TargetCollisionShape` | the owning GameObject (`Update()`) | collision systems (RO) | ECS systems writing target transform |
| `ProjectileSpawnEvent` queue/buffer | producers (timed, collisions, `CombatRoot`) | `ProjectileSpawnExpansionSystem` (drains) | apply reading raw events |
| `ProjectileSpawnCommand` containers (per shape) | `ProjectileSpawnExpansionSystem` | the matching per-shape apply | any system mutating after finalize; apply reading another shape's container |
| `NativeQueue<DamageReplayEvent>` | both collisions (parallel append) | damage-finalize owner → `NativeArray` → `DamageDispatchBridge` | reading the queue while collisions still write |
| managed `TargetCompanion` | the GameObject (create) | `DamageDispatchBridge` only (§8.4) | collision / tracking / spawn / vfx touching it |
| `Active` + `CombatLifetimeComponent` enabled state | apply (set on spawn), lifetime + collision (disable) | queries | structural add/remove during reuse |

## Job dependency constraints
- No structural changes inside jobs; cold-create uses ECB played back on the main thread once per apply system (one sync point per phase).
- Reuse jobs write only to slots matched by `WithDisabled<Active>` + domain/shape markers + shared-component filter; `[NativeDisableContainerSafetyRestriction]` on chunk handles as the current spawn jobs do.
- Container hand-off across systems via `World.GetExistingSystemManaged<T>()` + the owner's exposed handle/`Complete()`.

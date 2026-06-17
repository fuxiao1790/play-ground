# Context 004 — Final System Ordering & Phase Rules

The order below is the **contract** (design §9). Local `[UpdateBefore]`/`[UpdateAfter]` attributes remain useful but must not contradict this. Every task that adds, renames, or moves a system must keep this order intact.

---

## SimulationSystemGroup

`OrderFirst = true` group (relative order among these is not relied upon, EXCEPT the single damage-clear owner — see below):
- `CombatTargetSyncSystem` — clears + fills the `CombatTargetElement` buffer from both roots. (unchanged)
- **Damage buffer clear** — exactly ONE system clears `CombatDamageElement` at frame start. Choose `ProjectileSimulationSystem` as the sole owner; `AoeSimulationSystem` must NOT also clear it (remove the duplicate clear — D-DAMAGE-CLEAR / T008).

Ordered simulation phases (design §9.1–9.10):

| Phase | System(s) | Notes |
|---|---|---|
| 9.1 Lifetime / active | `CombatLifetimeSystem` | unified; runs before any collision so expired entities don't collide |
| 9.2 Timed spawn production | `TimedProjectileSpawnSystem` | producer → `ProjectileSpawnEvent` queue |
| 9.3 Tracking / movement | `ProjectileTrackingSystem`, `ProjectileMovementSystem` | unchanged |
| 9.4 Projectile contact gate | `ProjectileContactGateSystem` | gate-state maintenance only |
| 9.4 Projectile collision | `ProjectileCollisionSystem` | gate inline; emits typed consequence events |
| 9.5 AoE contact gate | `AoeContactGateSystem` | |
| 9.5 AoE collision | `AoeCollisionSystem` | gate inline; emits typed consequence events |
| 9.8 Spawn expansion | `ProjectileSpawnExpansionSystem`, `AoeSpawnExpansionSystem` | events → commands; all math; runs AFTER all collision so same-frame impact consequences are included |
| 9.9 Spawn apply | `ProjectileSpawnApplySystem`, `AoeSpawnApplySystem` | reuse / cold-create; LAST simulation write of new entities |
| (pulse vfx) | `AoePulseVfxSystem` | any time after lifetime; before render prepare |
| (render) | `CombatRenderPrepareSystem` | unchanged; after apply |

## PresentationSystemGroup
- `DamageDispatchBridge` (renamed `CombatHitDispatchSystem`) — replays `CombatDamageElement`.
- `CombatBatchedRenderSystem`, `CombatVfxDispatchSystem` — unchanged.

---

## Required ordering invariants

1. **Next-tick spawn (design R5):** every phase that could process a *new* entity (9.1 lifetime, 9.2 timed-spawn, 9.3 tracking/movement, 9.4/9.5 collision) MUST run before spawn apply (9.9). Entities created/reused in 9.9 therefore do not move, collide, or emit until the next frame. This is enforced **by order only** — there is no `SpawnedThisTick` marker.
2. **Producers before expansion:** `TimedProjectileSpawnSystem`, `ProjectileCollisionSystem`, and `AoeCollisionSystem` (all event producers) MUST run before `ProjectileSpawnExpansionSystem`. `AoeCollisionSystem` and `ProjectileCollisionSystem` (impact-AoE producers) MUST run before `AoeSpawnExpansionSystem`.
3. **Expansion before apply:** each domain's expansion runs before its apply (apply consumes the command output).
4. **Collision before expansion (cross-domain):** projectile collision can produce AoE events and AoE collision can produce projectile events, so BOTH collisions must precede BOTH expansions (matches today's `[UpdateAfter(ProjectileCollisionSystem)] [UpdateAfter(AoeCollisionSystem)]` on `ProjectileMultiExpandSystem` and the AoE spawn ordering).
5. **Lifetime before collision:** so a just-expired entity is excluded from collision the same frame (preserves current `ProjectileLifetimeSystem [UpdateBefore ProjectileCollisionSystem]`).

## Read/write permissions by data type

| Data | May write | May read | Forbidden |
|---|---|---|---|
| `CombatTargetElement` | `CombatTargetSyncSystem` only | collision systems (RO) | anything else writing it |
| `ProjectileSpawnEvent` queue/buffer | producers (timed, collisions, `CombatRoot`) | `ProjectileSpawnExpansionSystem` (drains) | apply systems reading raw events |
| `ProjectileSpawnCommandData` output | `ProjectileSpawnExpansionSystem` | `ProjectileSpawnApplySystem` | any system mutating it after finalize |
| `CombatDamageElement` | collision flush job (append); one `OrderFirst` system (clear) | `DamageDispatchBridge` | second clear owner; gameplay loops over it on main thread outside the bridge |
| managed `ICombatTarget` companion | — | `DamageDispatchBridge` only (design §8.4) | collision / tracking / spawn / vfx touching it |
| active tags + `CombatLifetimeComponent` enabled state | apply (set on spawn), lifetime + collision (disable) | queries | structural add/remove during reuse |

## Job dependency constraints
- No structural changes inside jobs; cold-create uses ECB played back on the main thread once per apply system (one sync point per phase).
- Reuse jobs write only to slots matched by `WithDisabled<...ActiveTag>` + shared-component filter; they use `[NativeDisableContainerSafetyRestriction]` on chunk handles exactly as the current spawn jobs do.
- Container hand-off across systems is via `World.GetExistingSystemManaged<T>()` + the owner's exposed handle/`Complete()` (today's MultiExpand↔SpawnSystem pattern).

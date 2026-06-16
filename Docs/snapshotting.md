# Hit-Spawn Fire-Time Snapshot — Design and Implementation

Status: final decision for Phase 3 (permanent doc)

Current implementation note: hit output is split into two scope-buffer lanes —
flattened `CombatDamageElement` (damage) and the internal `CombatPendingSpawn`
stream (cross-domain follow-up spawns), the latter converted directly into
`ProjectileSpawnRequestElement` / `AoeSpawnRequestElement` the same frame. There
is no `CombatSpawnElement` buffer and no managed spawn-routing step.

Summary
- Cross-domain hit-spawn behavior (projectile -> AOE, AOE -> projectile, domain-to-domain effects) must be safe, deterministic, and compatible with Burst/Jobs-driven ECS simulation. To ensure lifetime/version safety and simple, fast collision-time code, we use fat fire-time snapshot payloads authored at the moment an attack is fired. This document records the decision, rationale, data model, implementation guidance, and test requirements.

Decision
- Use Option A: fat fire-time snapshot payloads. All data required later to emit cross-domain spawn commands is copied into plain-data payload fields on the spawn command and carried through projectile/AOE entities into the collision-time hit streams. No live managed object lookups (no `GameObject`, `Transform`, `Collider2D`, or managed attack callbacks) are invoked during ECS collision. Damage replay (mapping `TargetId` to a live `ICombatTarget` and rolling crit) happens in `CombatHitDispatchSystem`, an ECS system in `PresentationSystemGroup` — not a MonoBehaviour `LateUpdate()`. Cross-domain follow-up spawns no longer cross to managed code at all: `CombatSpawnConvertJob` converts them directly into ECS spawn requests on the producing scope, same frame.

Why this choice
- Lifetime safety: in-flight projectiles must not depend on the continued existence or unmutated state of authored attack objects.
- Determinism: collision systems operate using plain, immutable data; no pointer-chasing or version semantics to reason about in jobs.
- Performance: collision and spawn-path code stays allocation-free and cache-friendly; the only step that still crosses to managed code is small (damage replay) and runs off the hot path.
- Implementation complexity: easiest to reason about and test; matches existing direct-damage snapshotting behavior.

Options considered (short)
- Option A — fat snapshot payloads: chosen. Tradeoffs: larger memory and copying vs speed and safety.
- Option B — runtime lookup / pointer chase: smaller payloads, but introduces cache misses, lifetime/version complexity, and poor Job/Burst compatibility.
- Option C — managed authoring callback: flexible and editor-friendly but unsafe for in-flight entities, not Burst-compatible, and can reference mutated/dead managed objects.

Required rules and constraints
- Snapshot payloads must contain only blittable, plain-data fields (integers, floats, enums, small fixed-size value structs, ids) — NO managed references, strings, or GC handles.
- Cross-domain snapshot data may contain type/template ids and parameter primitives (damage, count, spread, lifetime, target mask, position, direction, source node id, shape/geometry, effect flags).
- Projectile/AOE ECS collision systems never call into authoring objects. They only read snapshot data off the entity and write plain records into two `NativeStream` lanes: `CombatPendingDamage` and `CombatPendingSpawn`.
- `CombatHitFlushJob` drains the damage lane into the scope's `CombatDamageElement` buffer. `CombatSpawnConvertJob` drains the spawn lane and appends directly to `ProjectileSpawnRequestElement` / `AoeSpawnRequestElement` on the producing scope (`pending.Scope`) — both jobs are plain Burst `IJob`s, not managed code.
- `CombatHitDispatchSystem` (`PresentationSystemGroup`) is the only step that crosses back to managed code, and only for the damage lane: it maps `TargetId` to a live `ICombatTarget` via the scope's target registry and calls `ICombatTarget.ReceiveHits`.
- Cross-domain spawn payloads must be non-chaining by shape: a projectile or AOE entity carries at most one cross-domain spawn payload. `CombatHitPayload` (stack effect, crit, damage) is NOT a cross-domain spawn payload — it is always present. Only `ImpactAoe`, `ImpactProjectile` (on projectiles), and `ProjectileBurst` (on AOEs) are spawn payloads and count toward the limit. The non-chaining rule is enforced structurally by the snapshot types themselves (see Actual data model below), not by a runtime router check.

Payload separation rule
Every combat entity (projectile or AOE) carries exactly two categories of snapshot data:

1. CombatHitPayload — always present, same shape for all domains:
   - DamageAmount, CritChance, CritMultiplier — damage and crit roll inputs
   - DirectDamageEnabled — whether to apply direct HP loss
   - SourceNodeId — authoring skill/node that fired this entity
   - StackEffect — debuff stack trigger (accumulates on target; explosion spawned by target-side logic when threshold reached, not by the collision system directly)

2. Cross-domain spawn payload — at most one per entity type:
   - Projectile entity: ImpactAoe OR ImpactProjectile (not both)
   - AOE entity: ProjectileBurst (only)
   - Impact-spawned child entities: same rule applies — an impact projectile can still carry its own `ImpactAoe` (one more hop), but its snapshot type has no `ImpactProjectile` field, so it cannot chain into another impact projectile. AOE projectile bursts carry no spawn payload field at all.

CombatHitPayload is snapshotted at fire time and carried unchanged through the ECS lifetime of the entity. It is NOT a cross-domain spawn payload; it does not count toward the one-payload limit.

Actual data model (as implemented)

CombatHitPayload — shared blittable struct, `System.Common` namespace (`CombatEcsComponents.cs`):
```csharp
public struct CombatHitPayload
{
    public float DamageAmount;
    public float CritChance;
    public float CritMultiplier;
    public bool DirectDamageEnabled;
    public EntityId SourceNodeId;
    public CombatStatusEffectSnapshot StackEffect;
}
```

There is no longer a separate `CombatHitComponent` carrying only a target mask.
Target masking is resolved earlier, when each scope's `CombatTargetElement`
buffer is populated: `CombatRoot.CanTarget` is passed as the `additionalFilter`
to `CombatTargetSync<T>.SyncToBuffer`, so a scope's target buffer only ever
contains targets that root's faction is allowed to hit. Collision systems read
target entries as-is with no further per-entity mask check.

Projectile entity — hit data on `ProjectileHitComponent` (`System.Projectile`):
```csharp
public struct ProjectileHitComponent : IComponentData
{
    public int PierceRemaining;
    public float RepeatHitCooldownSeconds;
    public ProjectileHitPayload HitPayload;
}

// ProjectileHitPayload wraps CombatHitPayload:
public readonly struct ProjectileHitPayload
{
    public CombatHitPayload HitPayload;                          // fire-time hit snapshot
    public ProjectileImpactAoeSnapshot ImpactAoe;                 // cross-domain spawn (optional)
    public ProjectileImpactProjectileSnapshot ImpactProjectile;   // cross-domain spawn (optional)
    // passthrough properties (DamageAmount, CritChance, etc.) forward to HitPayload
}
```
Rule: ImpactAoe and ImpactProjectile are mutually exclusive by authoring convention (only one should be enabled per entity); both expose `Enabled` from a `typeId >= 0` check.

AOE entity — hit data on `AoeHitSpawnComponent` (`System.Aoe`):
```csharp
public struct AoeHitSpawnComponent : IComponentData
{
    public CombatHitPayload HitPayload;               // fire-time hit snapshot
    public AoeProjectileBurstSnapshot ProjectileBurst; // cross-domain spawn (optional); carries no further spawn payload
}
```

Hit/spawn streams — written by collision systems, drained by separate jobs (`System.Common/CombatHitElement.cs`):
- `CombatPendingDamage` (transient `NativeStream` payload): `Scope`, `SourceId`, `TypeId`, `TargetId`, `Position`, `Kind`, `DamageAmount`, `CritChance`, `CritMultiplier`, `DirectDamageEnabled`, `SourceNodeId`, `StackEffect`. Drained by `CombatHitFlushJob` into the scope's `CombatDamageElement` buffer (same field shape, `IBufferElementData`).
- `CombatPendingSpawn` (transient `NativeStream` payload): `Scope`, `SourceId`, `TypeId`, `TargetId`, `Position`, `TargetPosition`, `Kind`, `SourceNodeId`, `ImpactAoe`, `ImpactProjectile`, `ProjectileBurst`. Drained by `CombatSpawnConvertJob`, which writes directly into `AoeSpawnRequestElement`/`ProjectileSpawnRequestElement` on `pending.Scope` — no intermediate buffer, no managed step.

Data flow — projectile hit:
```
ProjectileSpawnCommand
  └─ CombatHitPayload (damage, crit, stackEffect, sourceNodeId)
  └─ ImpactAoe / ImpactProjectile (at most one)
       │
       ▼ CombatRoot.Spawn(ProjectileSpawnCommand) → ProjectileSpawnRequestElement
       ▼ ProjectileSpawnSystem → ProjectileHitComponent on ECS entity
       ▼ ProjectileCollisionSystem → writes CombatPendingDamage (always) and, if a
         spawn payload is enabled, CombatPendingSpawn — both via NativeStream.ParallelWriter
       ▼ CombatHitFlushJob → CombatDamageElement on the scope
       ▼ CombatSpawnConvertJob → ProjectileSpawnRequestElement / AoeSpawnRequestElement
         on the same scope, same frame (ECS only, no managed routing)
       ▼ CombatHitDispatchSystem (PresentationSystemGroup) → ICombatTarget.ReceiveHits
```

Data flow — AOE hit:
```
AoeSpawnCommand
  └─ CombatHitPayload (damage, crit, stackEffect, sourceNodeId) — built in CombatRoot.Spawn(AoeSpawnCommand)
  └─ ProjectileBurst (optional)
       │
       ▼ CombatRoot.Spawn(AoeSpawnCommand) → AoeSpawnRequestElement
       ▼ AoeSpawnSystem → AoeHitSpawnComponent on ECS entity
       ▼ AoeCollisionSystem → writes CombatPendingDamage and, if ProjectileBurst is
         enabled, CombatPendingSpawn — same two NativeStream lanes as the projectile path
       ▼ CombatHitFlushJob → CombatDamageElement on the scope
       ▼ CombatSpawnConvertJob → ProjectileSpawnRequestElement on the same scope, same frame
       ▼ same CombatHitDispatchSystem replay as projectile
```

Stack effect resolution (not a spawn payload):
- StackEffect is part of `CombatHitPayload`, carried unchanged through `CombatPendingDamage` into `CombatDamageElement`.
- `CombatHitDispatchSystem` packages it into `CombatHitData.StackEffect` and passes it to the target's `ICombatTarget.ReceiveHits`.
- `MobRoot.ReceiveHits` checks `hit.StackEffect.Enabled` and calls `ApplyStackEffect`, which accumulates stacks in `MobDebuffStackState`. When the threshold is reached, `MobRoot` resets the counter and calls `aoeCombatRoot.Spawn(new AoeSpawnCommand(...))` directly — the explosion is spawned by the target (a managed `CombatRoot` reference held on `MobRoot`), not by the collision system.
- This means StackEffect is a hit payload field, not a cross-domain spawn payload, and does not violate the one-payload limit. It is also the one remaining place a target-side MonoBehaviour still calls `CombatRoot.Spawn` directly, outside the ECS spawn-convert path.

Replay / Dispatch
- `CombatHitDispatchSystem` (`PresentationSystemGroup`) drains each scope's `CombatDamageElement` buffer:
  - Groups **consecutive** entries sharing the same `TargetId` (not a full sort/aggregate across the buffer).
  - Maps `TargetId` to a live `ICombatTarget` via the scope's `CombatDamageTargetSource.TargetsById` dictionary.
  - Rolls crit per entry with `UnityEngine.Random.value < hit.CritChance` inside the replay loop — still a managed-side, non-deterministic, per-hit-event cost; see [ecs-notes.md](./simulation/ecs-notes.md) Collision Event Dispatch section for the open aggregation problem this leaves.
  - Calls `target.ReceiveHits(hitDataScratch)` once per consecutive target group, then clears the buffer.
- `CombatSpawnConvertJob` handles all cross-domain follow-up spawns entirely in ECS — there is no managed mapping step, no `CombatSpawnRouter`, and no `HitEffect` event. Distinct integer salts (`ImpactAoeIdSalt`, `ImpactProjectileIdSalt`, `ProjectileBurstIdSalt`) keep the three deterministic id spaces from colliding when hashing `(SourceId, TypeId, TargetId)` into a spawn id.
- Cross-domain non-chaining enforcement: `ProjectileImpactProjectileSnapshot` carries its own `ImpactAoe` field but no `ImpactProjectile` field, and `AoeProjectileBurstSnapshot` carries neither — the struct shapes themselves make deeper chaining impossible to express, rather than a runtime router refusing to forward a payload.

Memory and performance guidance
- Pack payloads tightly: use enums/bitfields and small integer ids where possible.
- Prefer template/type ids over string names — resolve templates during spawn-command construction or `CombatSpawnConvertJob`, not in collision.
- Avoid per-hit allocations: `CombatPendingDamage`/`CombatPendingSpawn` streams and scope buffers are preallocated/sized per frame and recycled, not grown per hit.
- Be conservative with large arrays in payloads; prefer parameterized templates (type id + small parameter set).
- `CombatPendingSpawn` is only written when a projectile/AOE entity actually has a spawn payload enabled — most hits only ever touch the damage lane, keeping the common (damage-only) case small.

API and implementation checklist
- Authoring: spawn commands (`ProjectileSpawnCommand`, `AoeSpawnCommand`) accept individual damage/crit/stackEffect/sourceNodeId parameters and build `CombatHitPayload` internally at construction time.
- Spawn path: `CombatRoot.Spawn(ProjectileSpawnCommand)` and `CombatRoot.Spawn(AoeSpawnCommand)` build the request via private `ProjectileRequestFor`/`AoeRequestFor` helpers and append to the shared scope's `ProjectileSpawnRequestElement`/`AoeSpawnRequestElement` buffer (blittable data only).
- Spawn system: `ProjectileSpawnSystem` and `AoeSpawnSystem` write the hit payload into domain-specific ECS components (`ProjectileHitComponent.HitPayload`, `AoeHitSpawnComponent.HitPayload`).
- Collision: `ProjectileCollisionSystem` reads from `ProjectileHitComponent.HitPayload`; `AoeCollisionSystem` reads from `AoeHitSpawnComponent.HitPayload`. Both write plain records into the `CombatPendingDamage`/`CombatPendingSpawn` stream lanes via `NativeStream.ParallelWriter` — no managed calls.
- Flush: `CombatHitFlushJob` writes `CombatDamageElement` from `CombatPendingDamage`. `CombatSpawnConvertJob` writes `ProjectileSpawnRequestElement`/`AoeSpawnRequestElement` from `CombatPendingSpawn`.
- Replay: `CombatHitDispatchSystem` (in `System/Common/CombatHitDispatchSystem.cs`) drains `CombatDamageElement` and dispatches to `ICombatTarget.ReceiveHits`.
- Root routing: with one shared `CombatScope` per faction, internal follow-up spawns always target the producing scope (`pending.Scope`) directly — there is no separate routing component and no `GameRoot`-wired spawn router.

Testing checklist
- Unit/PlayMode tests:
  - Fire projectile with impact-AOE snapshot; confirm hit produces AOE via `CombatSpawnConvertJob` on the same frame.
  - Fire projectile with stackEffect; confirm stacks accumulate on target and explosion fires at threshold.
  - Mutate the authoring attack after firing; verify in-flight projectiles still use original snapshot.
  - Ensure cross-domain spawned projectiles/AOEs do not carry further `ImpactProjectile` payloads (non-chaining behavior).
  - Confirm `CombatDamageElement` carries only damage/crit/status fields needed for replay; no managed references leak into the buffer.
  - Stress test: high volume projectile hits produce stable memory and reasonable performance counters.

Notes
- StackEffect triggers an explosion via target-side state (`MobDebuffStackState`), not via the collision system. This is intentional: the collision system stays stateless and allocation-free; the only spawn decision still made by managed code is this target-side stack threshold check.
- If future features require deep, content-driven chaining, add an explicit authored combo system rather than enabling unrestricted runtime chaining.
- Consider a compact binary payload encoding for extremely hot paths (e.g., 16-byte payloads) if memory proves limiting.

End of document

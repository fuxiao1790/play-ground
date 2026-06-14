# Hit-Spawn Fire-Time Snapshot — Design and Implementation

Status: final decision for Phase 3 (permanent doc)

Current implementation note: hit output is split into flattened
`CombatDamageElement` and `CombatSpawnElement` buffers. Older diagram text in
this doc may still use the previous `CombatHitElement` side-buffer names.

Summary
- Cross-domain hit-spawn behavior (projectile -> AOE, AOE -> projectile, domain-to-domain effects) must be safe, deterministic, and compatible with Burst/Jobs-driven ECS simulation. To ensure lifetime/version safety and simple, fast collision-time code, we use fat fire-time snapshot payloads authored at the moment an attack is fired. This document records the decision, rationale, data model, implementation guidance, and test requirements.

Decision
- Use Option A: fat fire-time snapshot payloads. All data required later to emit cross-domain spawn commands is copied into plain-data payload fields on the spawn command and carried through projectile entities and hit elements. No live managed object lookups (no `GameObject`, `Transform`, `Collider2D`, or managed attack callbacks) are invoked during ECS collision; replay is performed from copied snapshot data in `LateUpdate()` on the managed root.

Why this choice
- Lifetime safety: in-flight projectiles must not depend on the continued existence or unmutated state of authored attack objects.
- Determinism: collision systems operate using plain, immutable data; no pointer-chasing or version semantics to reason about in jobs.
- Performance: collision and spawn-path code stays allocation-free and cache-friendly; replay/emit is a small, managed step off the hot path.
- Implementation complexity: easiest to reason about and test; matches existing direct-damage snapshotting behavior.

Options considered (short)
- Option A — fat snapshot payloads: chosen. Tradeoffs: larger memory and copying vs speed and safety.
- Option B — runtime lookup / pointer chase: smaller payloads, but introduces cache misses, lifetime/version complexity, and poor Job/Burst compatibility.
- Option C — managed authoring callback: flexible and editor-friendly but unsafe for in-flight entities, not Burst-compatible, and can reference mutated/dead managed objects.

Required rules and constraints
- Snapshot payloads must contain only blittable, plain-data fields (integers, floats, enums, small fixed-size value arrays, ids) — NO managed references, strings, or GC handles.
- Cross-domain snapshot data may contain type/template ids and parameter primitives (damage, count, spread, lifetime, routing/team id, position, direction, source id, target_mask, effect flags).
- Projectile/AOE ECS systems never call into authoring objects. They only read snapshot data and emit `SpawnRequest` records to scope buffers.
- Replay (managed mapping to typed spawn APIs and live GameObject-based prefabs) happens in `LateUpdate()` on the owning root (e.g., `ProjectileRoot.LateUpdate()`, `AoeRoot.LateUpdate()`), which reads the hit buffers and maps snapshot payloads into managed `SpawnCommand` calls.
- Cross-domain spawn commands must be non-chaining by shape: a replayed cross-domain spawn may carry at most one cross-domain spawn payload. A `CombatHitPayload` (stack effect, crit, damage) is NOT a cross-domain spawn payload — it is always present. Only `ImpactAoe`, `ImpactProjectile`, and `ProjectileBurst` are spawn payloads and count toward the limit.

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
   - Impact-spawned child entities: same rule applies — one cross-domain spawn payload max

CombatHitPayload is snapshotted at fire time and carried unchanged through the ECS lifetime of the entity. It is NOT a cross-domain spawn payload; it does not count toward the one-payload limit.

Actual data model (as implemented)

CombatHitPayload — shared blittable struct, System.Common namespace:
```csharp
public struct CombatHitPayload
{
    public float DamageAmount;
    public float CritChance;
    public float CritMultiplier;
    public bool DirectDamageEnabled;
    public EntityId SourceNodeId;
    public CombatStackEffectSnapshot StackEffect;
}
```

CombatHitComponent — ECS component shared by all domains, targeting filter only:
```csharp
public struct CombatHitComponent : IComponentData
{
    public int TargetMask;   // bitmask; no hit data here
}
```

Projectile entity — hit data on ProjectileHitComponent:
```csharp
// ProjectileHitComponent.HitPayload is ProjectileHitPayload, which wraps CombatHitPayload:
public readonly struct ProjectileHitPayload
{
    public CombatHitPayload HitPayload;          // fire-time hit snapshot
    public ProjectileImpactAoeSnapshot ImpactAoe;           // cross-domain spawn (optional)
    public ProjectileImpactProjectileSnapshot ImpactProjectile; // cross-domain spawn (optional)
    // passthrough properties (DamageAmount, CritChance, etc.) forward to HitPayload
}
```
Rule: ImpactAoe and ImpactProjectile are mutually exclusive by authoring convention (only one should be enabled per entity).

AOE entity — hit data on AoeHitSpawnComponent:
```csharp
public struct AoeHitSpawnComponent : IComponentData
{
    public CombatHitPayload HitPayload;               // fire-time hit snapshot
    public AoeProjectileBurstSnapshot ProjectileBurst; // cross-domain spawn (optional)
}
```

Hit element buffers — written by flush jobs after collision:
- CombatDamageElement: flattened damage/status record; contains DamageAmount, CritChance, CritMultiplier, DirectDamageEnabled, SourceNodeId, and StackEffect.
- CombatSpawnElement: flattened internal spawn record; contains source/target ids, hit position, target snapshot position, and ImpactAoe/ImpactProjectile/ProjectileBurst payloads.

Data flow — projectile hit:
```
ProjectileSpawnCommand
  └─ CombatHitPayload (damage, crit, stackEffect, sourceNodeId)
  └─ ImpactAoe / ImpactProjectile (at most one)
       │
       ▼ ProjectileRoot.SpawnRequestFor → ProjectileSpawnRequestElement
       ▼ ProjectileSpawnSystem → ProjectileHitComponent on ECS entity
       ▼ ProjectileCollisionSystem → CombatPendingHit (copies all payload fields)
       ▼ CombatHitFlushJob → CombatHitElement + side-buffers
       ▼ CombatHitDispatchSystem → CombatHitReplay.ReplayAndClear
       ▼ ProjectileHitReplayAdapter.ReplayEffect → CombatSpawnRouter.HitEffect event
       ▼ CombatSpawnRouter.SpawnImpactProjectiles / SpawnImpactAoe
```

Data flow — AOE hit:
```
AoeSpawnCommand
  └─ CombatHitPayload (damage, crit, stackEffect, sourceNodeId) — built in AoeRoot.SpawnRequestFor
  └─ ProjectileBurst (optional)
       │
       ▼ AoeRoot.SpawnRequestFor → AoeSpawnRequestElement
       ▼ AoeSpawnSystem → AoeHitSpawnComponent on ECS entity
       ▼ AoeCollisionSystem → CombatPendingHit (reads hitSpawn.HitPayload.*)
       ▼ CombatHitFlushJob → CombatHitElement + side-buffers
       ▼ same replay chain as projectile
```

Stack effect resolution (not a spawn payload):
- StackEffect is part of CombatHitPayload, written to CombatHitPayloadElement via CombatHitFlushJob.
- Replay delivers it via CombatHitData.StackEffect to the target's ReceiveHit method.
- MobRoot.ApplyStackEffect accumulates stacks in MobDebuffStackState. When count >= StackThreshold, MobRoot resets the counter and calls aoeRoot.Spawn — the explosion is spawned by the target, not the collision system.
- This means StackEffect is a hit payload field, not a cross-domain spawn payload, and does not violate the one-payload limit.

Replay / Managed routing
- `ProjectileRoot.LateUpdate()` drains `CombatHitElement` buffer and for each element:
  - Map `TargetId` back to the managed `ICombatTarget` (via scope target registry).
  - Roll crit from `hit.CritChance` / `hit.CritMultiplier`.
  - If `hit.EffectIndex >= 0`: fetch `CombatHitEffectElement` and call `adapter.ReplayEffect` → `CombatSpawnRouter` dispatches the cross-domain spawn.
  - If `hit.PayloadIndex >= 0`: fetch `CombatHitPayloadElement` and pass `StackEffect` to `ICombatTarget.ReceiveHits` via `CombatHitData`.
- Cross-domain non-chaining enforcement: `CombatSpawnRouter.SpawnImpactProjectiles` passes the impact snapshot's own `ImpactAoe` and `StackEffect` into the child `ProjectileSpawnCommand` but does NOT forward `ImpactProjectile` recursively beyond one hop.

Memory and performance guidance
- Pack payloads tightly: use enums/bitfields and small integer ids where possible.
- Prefer template/type ids over string names — resolve templates during the managed replay step.
- Avoid per-hit allocations: hit buffers should be preallocated and recycled per scope entity to avoid GC.
- Be conservative with large arrays in payloads; prefer parameterized templates (type id + small parameter set).
- CombatHitPayloadElement and CombatHitEffectElement are sparse side-buffers — only written when the corresponding feature is enabled. This keeps CombatHitElement small and cache-friendly for the common (damage-only) case.

API and implementation checklist
- Authoring: spawn commands (`ProjectileSpawnCommand`, `AoeSpawnCommand`) accept individual damage/crit/stackEffect/sourceNodeId parameters and build `CombatHitPayload` internally at construction time.
- Spawn path: `ProjectileRoot.SpawnRequestFor` and `AoeRoot.SpawnRequestFor` copy the payload into the request element (blittable data only).
- Spawn system: `ProjectileSpawnSystem` and `AoeSpawnSystem` write `CombatHitPayload` to domain-specific ECS components (`ProjectileHitComponent.HitPayload`, `AoeHitSpawnComponent.HitPayload`). `CombatHitComponent` carries only `TargetMask`.
- Collision: `ProjectileCollisionSystem` reads from `ProjectileHitComponent.HitPayload`; `AoeCollisionSystem` reads from `AoeHitSpawnComponent.HitPayload`. Neither reads damage/crit data from `CombatHitComponent`.
- Flush: `CombatHitFlushJob` writes `CombatHitElement` and optional side-buffers from `CombatPendingHit`.
- Replay: `CombatHitReplay.ReplayAndClear` (in `System/Common/CombatHitReplay.cs`) drains all three buffers and dispatches to `ICombatHitReplayAdapter`.
- Root routing: `CombatSpawnRouter` (wired by `GameRoot`) maps effect/template ids and team routing to destination roots (player→mob roots, mob→player roots).

Testing checklist
- Unit/PlayMode tests:
  - Fire projectile with impact-AOE snapshot; confirm hit produces AOE via replay on next frame.
  - Fire projectile with stackEffect; confirm stacks accumulate on target and explosion fires at threshold.
  - Mutate the authoring attack after firing; verify in-flight projectiles still use original snapshot.
  - Ensure cross-domain spawned projectiles/AOEs do not carry further ImpactProjectile payloads (non-chaining behavior).
  - Confirm CombatHitComponent carries only TargetMask; no damage/crit data on that component.
  - Stress test: high volume projectile hits produce stable memory and reasonable performance counters.

Notes
- StackEffect triggers an explosion via target-side state (MobDebuffStackState), not via the collision system. This is intentional: the collision system stays stateless and allocation-free; all spawn decisions happen in managed LateUpdate.
- If future features require deep, content-driven chaining, add an explicit authored combo system rather than enabling unrestricted runtime chaining.
- Consider a compact binary payload encoding for extremely hot paths (e.g., 16-byte payloads) if memory proves limiting.

End of document

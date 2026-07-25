# 009 — External spawn event + ECS resource gate

## Goal
Move the resource check to where GameObject-initiated spawns enter ECS. The
GameObject emits an **external spawn event** (spawn intent) during Update; an ECS
gate system reads the list, checks and deducts the caster's resource, and either
emits the **internal ECS spawn events** (accept) or a **rejection event** (reject).
Rejections go back to the GameObject; acceptance simply spawns.

## Why this shape (grounding)
Today `CombatRoot.SpawnRegisteredProjectile/SpawnRegisteredAoe` run on the managed
thread and **directly append** the internal `ProjectileSpawnEvent` /
`AoeSpawnEvent` to the scope entity buffer (`CombatRoot.cs` L207-291) — no resource
check. Interval children instead emit those same internal events from
`TimedSpawnSystem`. So "external spawn event → internal ECS spawn event" is a new
gated intake step inserted only in front of the *GameObject-initiated* path; the
timed/impact/child paths already produce internal events and are unaffected (they
never spend the pool).

## Design

New external intent type(s) — mirror the existing projectile/AOE split so they
reuse the same per-instance fields, plus caster + cost:
```csharp
public struct ExternalSpawnRequest {           // one type with a kind, or two typed events
    public IntervalChildKind Kind;             // Projectile / ImpactAoe / LingeringAoe
    public Hash128 TemplateKey;
    public Entity Caster;                       // caster proxy entity (resource owner)
    public float ManaCost;
    public float2 Position; public float2 AimDirection;
    public int Count; public CombatFaction Faction;
    public int CastToken;                       // echoed on rejection
}
public struct SpawnRejectedEvent { public Entity Caster; public int CastToken; /* reason */ }
```

Lanes / submission (reuse the scope-entity buffer + result-lane patterns):
- External requests: a buffer/queue on the scope entity (same place `CombatRoot`
  already appends spawn events).
- `CombatRoot.SpawnRegistered*` change: append an `ExternalSpawnRequest` instead of
  the internal event directly; new params `Entity caster, float manaCost, int
  castToken`. (Keep a non-gated internal append for callers that must bypass the
  gate, if any.)
- Rejection results: `SpawnRejectedSingleton { NativeList<SpawnRejectedEvent>;
  ProducerHandle }`, drained by a presentation bridge (below).

System — `ExternalSpawnGateSystem : ISystem`, `SimulationSystemGroup`, ordered
**before** the projectile/AOE expansion systems and after nothing that would
race it; runs **single-threaded** (serial drain), so per-caster `Mana` writes never
race:
- For each external request: if caster has `Mana` and `Mana.Current >= ManaCost`,
  subtract and emit the internal spawn event exactly as `SpawnRegistered*` does
  today (stamp id/jitter, `Add` to `ProjectileSpawnEvent` / append `AOE spawn
  event`). No `Mana` component → treat as accept (no gating; avoids soft-lock).
- Else: append a `SpawnRejectedEvent { Caster, CastToken }`; emit no spawn.

Rejection bridge — `SpawnRejectionBridge` (`PresentationSystemGroup`, like
`CombatApplyBridge`): drain rejections, resolve the caster via `TargetCompanion` →
`ICombatTarget`, hand the rejection (with `CastToken`) to its `SkillDriver`.
Acceptance is **not** signaled back — the spawn happening is the signal.

## Acceptance Criteria
- An external request with enough mana deducts `ManaCost` and produces the same
  internal spawn (projectile/AOE) as today; too little mana produces a
  `SpawnRejectedEvent`, no spawn, no deduction.
- Accepted spawns appear with the **same latency as the current root-cast path**
  (no extra frame); only rejections round-trip to the GameObject.
- Two casters in one frame affect only their own `Mana` (serial system).
- Missing `Mana` → accepted.
- Interval/impact/child spawns are unchanged and never gated.

## Dependencies
Depends on 004 (`Mana` component). Consumed by 010.

## Scope
Large (new intent type + gate system + rejection lane/bridge + `CombatRoot` API
change + tests).

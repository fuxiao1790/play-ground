# 001 — Spawn request + result contract

Define the two boundary data types and wire their containers onto the shared combat
scope. No producer or consumer logic yet — after this task the containers exist and
stay empty.

## Changes

### `CombatSpawnRequest` (new `IBufferElementData`)
Location: alongside the spawn event contracts (e.g.
`Assets/Scripts/System/Spawning/`, matching where `ProjectileSpawnEvent` /
`ImpactAoeSpawnEvent` live).

Unmanaged struct, one element per requested top-level spawn. Fields (mirror the union
of the existing spawn events + caster):

- `IntervalChildKind Kind` — `Projectile` / `ImpactAoe` / `LingeringAoe` (the same
  discriminator the events use).
- `Unity.Entities.Hash128 TemplateKey`
- `float2 Position`
- `float2 AimDirection`
- `CombatFaction Faction`
- `int SourceId` — the managed pre-allocated base id.
- `uint JitterSeed`
- `int ContactGateSeedTargetId`
- `Entity Caster` — the caster's target-proxy entity; `Entity.Null` when ownerless.

No managed references (invariant 2).

### `CombatSpawnResult` (new, minimal)
Unmanaged struct. **Empty/minimal for now** per the scoping decision — carries only
what the reply bridge needs to route:

- `Entity Caster` — proxy entity to resolve the managed caster.

Document inline that accept/reject, assigned id, and cost fields are added with mana.

### Result singleton lane (new `IComponentData`)
Mirror `CombatApplyResultSingleton`:

- `NativeList<CombatSpawnResult> Results`
- `JobHandle ProducerHandle`

Created/disposed by `SpawnIntakeSystem` (task 002). Declared here so both 002 and 004
compile against it.

### `CombatScopeOwner` wiring
In `CombatScopeOwner.Acquire`, add the request buffer to the scope entity next to the
existing event buffers:

```csharp
entityManager.AddBuffer<CombatSpawnRequest>(ownedScope);
```

Do **not** add the result lane here — the result lane is a system singleton (task
002), not scope-owned, matching `CombatApplyResultSingleton`.

Leave the existing `ProjectileSpawnEvent` / `ImpactAoeSpawnEvent` /
`LingeringAoeSpawnEvent` buffers in place for now; task 003 removes them once nothing
writes them.

## Acceptance criteria

- Project compiles; `CombatSpawnRequest` buffer is present on the scope after
  `Acquire`.
- No system reads or writes the request buffer or the result lane yet (empty,
  inert).
- No behavior change to existing spawns.

## Dependencies

None. Precedes 002, 003, 004.

## Scope / complexity

Small. Pure type + container declarations.

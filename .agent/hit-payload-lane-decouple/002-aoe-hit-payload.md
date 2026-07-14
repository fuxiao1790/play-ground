# 002 — AOE owns its hit payload

## Goal

Introduce `AoeHitPayload` and retype the AOE command + component off the shared
`CombatHitPayload`. Behavior-identical.

## Change

### New `AoeHitPayload` struct
Place it in the AOE lane (e.g. new `AoeHitPayload.cs` under
`Assets/Scripts/System/Aoes/`, or alongside `AoeEcsComponents.cs`). Plain
unmanaged struct, same six fields as today's shared payload:

```
public struct AoeHitPayload
{
    public float DamageAmount;
    public float CritChance;
    public float CritMultiplier;
    public bool DirectDamageEnabled;
    public EntityId SourceNodeId;
    public StackEffectSnapshot StackEffect;
}
```

Do **not** add `OnHitSpawnRef` here — it stays a sibling field on
`AoeHitSpawnComponent` / `AoeSpawnCommand`, preserving today's layout.

### Retype the containers
- **`AoeEcsComponents.cs:43`** — `AoeHitSpawnComponent.HitPayload`:
  `CombatHitPayload` → `AoeHitPayload`.
- **`AoeSpawnPipeline.cs:67`** — `AoeSpawnCommand.HitPayload`:
  `CombatHitPayload` → `AoeHitPayload`.

## Build/consume sites to update

1. **`CombatRoot.cs:487-495`** (AOE request → command) — `new CombatHitPayload{…}`
   → `new AoeHitPayload{…}` (fields unchanged, incl. `SourceNodeId = request.SourceNodeId`).
2. **`SkillDriver.cs:760-768`** (`BuildAoeTemplate`) — `new CombatHitPayload{…}` →
   `new AoeHitPayload{…}`.
3. **`CombatRoot.cs:594-598`** (`SpawnTemplateFor(AoeSpawnCommand)`) — type of the
   `hitPayload` local `CombatHitPayload` → `AoeHitPayload`; logic (zero
   `StackEffect.Faction`) unchanged.
4. **`AoeSpawnApplySystem.cs:584`** — `HitPayloadFor(CombatHitPayload hitPayload,
   CombatFaction faction)` → `AoeHitPayload`; body unchanged (field names identical).

## Unchanged (verify, don't edit)

- `AoeCollisionCore.EmitHit` reads `hitSpawn.HitPayload.DamageAmount / .CritChance /
  .CritMultiplier / .DirectDamageEnabled / .SourceNodeId / .StackEffect` — all
  resolve against `AoeHitPayload` (same field names). No edit.
- `OnHitSpawn` access paths unchanged (still a sibling field).

## Acceptance

- No AOE-lane production reference to `CombatHitPayload` remains.
- AOE spawn/collision/apply compiles and behaves identically.
- `AoeSimulationTests` / `AoePlayModeTests` pass after their construction/helper
  sites (incl. `FirstScopedAoeHitPayload` returning `AoeHitPayload`) are updated in
  [003](003-remove-shared-type-and-tests.md).

## Scope

Small–medium. One new struct + 2 retypes + 4 mechanical call-site edits.

## Depends on

None (parallel with 001; merges together with 003).

# 001 — Projectile owns its hit payload

## Goal

Make `ProjectileHitPayload` own the hit fields directly instead of wrapping the
shared `CombatHitPayload`. Behavior-identical.

## Change

### `ProjectileRuntimeEvents.cs`
Rewrite `ProjectileHitPayload` ([:15-35](../../Assets/Scripts/System/Projectiles/ProjectileRuntimeEvents.cs#L15-L35))
from a readonly wrapper to a plain-fields struct:

- Fields: `float DamageAmount; float CritChance; float CritMultiplier;
  bool DirectDamageEnabled; EntityId SourceNodeId; StackEffectSnapshot StackEffect;
  OnHitSpawnRef OnHitSpawn;`
- Drop the nested `CombatHitPayload HitPayload` property and the `(CombatHitPayload,
  OnHitSpawnRef)` constructor.
- Keep `DamageSnapshot Damage => new(DamageAmount);` as a computed readonly
  property (and any other helper) over the inlined fields.

Decision: plain public fields (not readonly + ctor) so build/test sites use
initializer syntax and match `AoeHitPayload` (002). If a readonly contract is
desired later it can be reintroduced, but uniform initializer syntax is worth more
here.

## Build/consume sites to update

1. **`ProjectileSpawnRequest.cs:100-110`** — replace
   `new ProjectileHitPayload(new CombatHitPayload { … }, onHitSpawn)` with a flat
   `new ProjectileHitPayload { DamageAmount = …, …, OnHitSpawn = onHitSpawn }`.
2. **`SkillDriver.cs:700-710`** (`BuildProjectileTemplate`) — same flattening.
3. **`CombatRoot.cs:571-576`** (`SpawnTemplateFor(ProjectileSpawnCommand)`) —
   currently unwraps `hp.HitPayload` (nested `CombatHitPayload`) to zero
   `StackEffect.Faction`, then rewraps. Rewrite to edit `template.HitPayload`'s
   inlined `StackEffect` field directly.
4. **`ProjectileSpawnApplySystem.cs:199`** — `CombatHitPayload hitPayload =
   cmd.HitPayload.HitPayload;` becomes a single hop against the flattened
   `ProjectileHitPayload` (read the fields directly, or `var hp = cmd.HitPayload;`).
   Follow how the local is used below :199 and adjust field access accordingly.

## Unchanged (verify, don't edit)

- `ProjectileHitComponent.HitPayload` stays typed `ProjectileHitPayload`.
- `ProjectileCollisionSystem` reads `projectileHit.HitPayload.DamageAmount /
  .CritChance / .CritMultiplier / .DirectDamageEnabled / .SourceNodeId /
  .StackEffect / .OnHitSpawn` — all still resolve (same field names). No edit.

## Acceptance

- No projectile-lane reference to `CombatHitPayload` remains.
- Projectile spawn/collision/apply compiles and behaves identically.
- `ProjectileCollisionSimulationTests` / `ProjectileSpawnPipelineTests` /
  `ProjectileTrackingSimulationTests` still pass after their construction sites are
  updated in [003](003-remove-shared-type-and-tests.md).

## Scope

Small–medium. One type rewrite + 4 mechanical call-site edits.

## Depends on

None (can be authored alongside 002; both must merge together with 003).

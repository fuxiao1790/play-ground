# 010 — SkillDriver emits external spawn events; handles rejections

## Goal
Route GameObject-initiated casts through the external-spawn gate (009) with minimal
change to the cast flow: fire optimistically (spawn intent + cooldown as today),
and react only to rejection events. Same path for player and mob.

## Current flow (`SkillDriver.Tick` → `SkillSpawnTranslator.Spawn`)
```
if slot.IsReady && fireHeld:
    SkillSpawnTranslator.Spawn(def, origin, aim, combatRoot, faction) // direct internal spawn
    slot.ResetOnFire()
```
`SkillSpawnTranslator.Spawn` calls `combatRoot.SpawnRegisteredProjectile/Aoe`
(direct internal event, no resource check).

## New flow
1. **Caster wiring.** `SkillDriver` gains the caster proxy `Entity`;
   `PlayerRoot`/`MobRoot` set it when the proxy is created (they own driver +
   proxy). Used as the `ExternalSpawnRequest.Caster`.
2. **Emit external spawn event.** `SkillSpawnTranslator.Spawn` (or the driver)
   calls the updated `CombatRoot.SpawnRegistered*` overload passing `caster`,
   `def.ManaCost` (compiled, from 001), and a fresh `CastToken`. Keep the existing
   optimistic behavior: `slot.ResetOnFire()` at fire time. `ManaCost <= 0` still
   goes through as a request with cost 0 (always accepted) — or short-circuits to a
   direct internal spawn; either is fine since 0 never rejects.
3. **Handle rejection.** `SkillDriver` receives `SpawnRejectedEvent`s (via the
   `SpawnRejectionBridge`, e.g. `ICombatTarget.ReceiveSpawnRejected(castToken)` or a
   direct driver hook). Default reaction: **refund readiness** for that slot (undo
   the cooldown reset so an out-of-mana press doesn't burn the cooldown), optional
   "no mana" feedback. Track the fired `CastToken` per slot to match the rejection.

### Notes
- Accepted casts need no callback — the projectile/AOE simply spawns (same latency
  as today). Only rejections are handled managed-side.
- Aim is baked into the external request at fire time, so there is no deferred-aim
  problem.
- Optional stricter variant (index open question 1): hold the cooldown reset until
  a frame passes with no rejection. Default is the simpler optimistic-refund model.

## Acceptance Criteria
- With enough mana: cast spawns at current latency; mana drops by `ManaCost`.
- With insufficient mana: nothing spawns, no mana change, and the slot's cooldown is
  refunded so it retries once mana regenerates (per default reaction).
- Zero-cost skills behave exactly as today.
- Mob casters use the identical path (their rejections handled the same way).

## Dependencies
Depends on 009 (external gate + rejection bridge) and 001 (`RuntimeSkillDefinition.ManaCost`).

## Scope
Medium (thin driver change + caster wiring + rejection handling; player + mob; tests).

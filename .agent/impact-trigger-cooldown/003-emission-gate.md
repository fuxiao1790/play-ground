# 003 — Gate the on-hit-spawn emission

Wrap the on-hit-spawn emission branches in a cooldown gate and set the countdown
on emit. This is where the behavior actually changes. Direct damage, VFX, pierce,
and contact-gate logic stay outside the gate.

## Projectile — `ProjectileCollisionSystem.ProjectileCollisionJob.Execute`

`projectileHit` is already `ref ProjectileHitComponent`. Around the two emission
blocks (lines ~286 `Kind == Projectile`, and ~311 `Kind == ImpactAoe/LingeringAoe`):

```csharp
OnHitSpawnRef onHitSpawn = projectileHit.HitPayload.OnHitSpawn;
bool onHitSpawnReady =
    onHitSpawn.Enabled
    && (onHitSpawn.CooldownSeconds <= 0f || projectileHit.OnHitSpawnCooldownRemaining <= 0f);

if (onHitSpawnReady)
{
    // existing Projectile-kind branch  (ProjectileEventWriter.Enqueue ...)
    // existing ImpactAoe/LingeringAoe branch (Impact/Lingering writer Enqueue ...)

    if (onHitSpawn.CooldownSeconds > 0f)
        projectileHit.OnHitSpawnCooldownRemaining = onHitSpawn.CooldownSeconds;
}
```

- Replace the current `projectileHit.HitPayload.OnHitSpawn.Enabled && Kind == ...`
  conditions with the branch structure above (they become the inner
  `Kind`-discriminator `if`s under `onHitSpawnReady`).
- The direct-damage `HitWriter.Enqueue` (line ~268), `VfxPending.Enqueue`
  (line ~351), `AddOrRefreshGate` and `PierceRemaining--` stay **outside** the
  gate — unchanged.
- The reset assignment lives inside `onHitSpawnReady`, so within a single
  multi-target pass only the first hit spawns; subsequent iterations see
  `OnHitSpawnCooldownRemaining > 0` and skip.

## AOE — `AoeCollisionCore.EmitHit`

`EmitHit` is shared by `LingeringAoeCollisionSystem` and
`ImpactAoeCollisionSystem`. Thread the cooldown through:

1. Change both AOE jobs' `Execute` param `in AoeHitSpawnComponent hitSpawn` →
   `ref AoeHitSpawnComponent hitSpawn` (queries already RW from task 002).
2. `RunCollision` and `EmitHit` currently take `AoeHitSpawnComponent hitSpawn`
   by value. Pass it `ref` (or pass `ref float onHitSpawnCooldownRemaining` +
   the `OnHitSpawnRef`) so the countdown write is visible on the entity. Simplest:
   change `RunCollision(..., in AoeHitSpawnComponent hitSpawn, ...)` →
   `ref AoeHitSpawnComponent hitSpawn`, and likewise `EmitHit`.
3. In `EmitHit`, gate the two spawn-event blocks (the `Kind == Projectile` block
   ~176 and the `ImpactAoe/LingeringAoe` block ~193):
   ```csharp
   bool onHitSpawnReady =
       hitSpawn.OnHitSpawn.Enabled
       && (hitSpawn.OnHitSpawn.CooldownSeconds <= 0f
           || hitSpawn.OnHitSpawnCooldownRemaining <= 0f);

   if (onHitSpawnReady)
   {
       // existing projectile-burst enqueue
       // existing impact/lingering AOE enqueue
       if (hitSpawn.OnHitSpawn.CooldownSeconds > 0f)
           hitSpawn.OnHitSpawnCooldownRemaining = hitSpawn.OnHitSpawn.CooldownSeconds;
   }
   ```
   - The direct-damage `hitWriter.Enqueue` (`HasHitEvent`) and the hit-VFX block
     stay outside the gate — unchanged.
   - `EmitHit` is called once per target inside `RunCollision`'s loop, so setting
     remaining on the first emit gates the remaining targets of the same pass.
4. **Impact (pulse) AOE**: `ImpactAoeCollisionJob` now passes `ref hitSpawn`; it
   deactivates after one pass, so the write is written-then-discarded — correct
   and harmless (fires once regardless of cooldown).

## Concurrency confirmation

`AoeHitSpawnComponent` is not enableable and is not taken as `EnabledRefRW`
anywhere, so `ref` on it is safe — no ref+EnabledRef UB
(`[[reference_ijob_ref_plus_enabledref]]`). Each entity is single-writer under
`ScheduleParallel`.

## Acceptance criteria (user-run PlayMode)

- **Projectile impact-AOE with cooldown**, piercing across N enemies in one frame:
  exactly **one** impact AOE spawns that frame; direct damage still lands on all N.
- **Lingering AOE on-hit projectile with cooldown = T**: over a lifetime with tick
  interval `t < T`, on-hit bursts fire at ~`T` spacing, not every tick; direct
  damage still applies every tick.
- **cooldownSeconds = 0**: impact spawn fires on every hit, identical to
  pre-change (regression check against existing `ProjectileCollisionSimulationTests`
  / `AoeSimulationTests`).

## Dependencies

- Tasks 001 (config on the ref) and 002 (per-entity field + RW query).
- Task 004 supplies the per-frame decrement — without it, remaining never returns
  to 0, so a cooldown'd source spawns once then never again. Land 003+004
  together before asserting the timed behavior.

## Scope

Medium. Two collision jobs + shared `AoeCollisionCore` signature change.

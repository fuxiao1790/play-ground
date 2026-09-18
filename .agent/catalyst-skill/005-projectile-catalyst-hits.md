# 005 — Projectile hits on catalysts

## Goal

A friendly projectile overlapping a catalyst spends pierce, gates its contact,
and fires the catalyst's own skill — with no combat hit event and no blocking.

## Changes

`Assets/Scripts/System/Catalysts/CatalystHitEmission.cs` (new, internal static)
Mirrors `ProjectileHitEmission` / `AoeCollisionCore` in structure and salt
discipline.
- `ScanProjectile(...)` — walks `CatalystProjectileCells` over a cell range
  computed from the projectile's query bounds expanded by `MaxCatalystRadius`
  (its own range; never widen the unit range), narrow-phases with
  `CombatCollisionMath.BoundsIntersect` then `CombatCollisionMath.Hit`, and for
  each overlap:
  1. friendly filter: `entry.Faction == identity.Faction` — the inverse of the
     unit rule. Enemy fire skips catalysts entirely.
  2. `ProjectileHitEmission.IsGated(contactGates, CatalystKey(entry.Catalyst))` →
     skip.
  3. `EnqueueTriggerSpawn(...)` — stamp `entry.OnHitSpawn` into the matching
     existing spawn queue (projectile / impact AOE / lingering AOE / targeted).
  4. `ProjectileHitEmission.AddOrRefreshGate(contactGates, key, projectileHit.RepeatHitCooldownSeconds)`.
  5. `projectileHit.PierceRemaining--`; when it drops below zero, deactivate
     through the existing `ProjectileHitEmission.Deactivate` funnel.
- **No `HitWriter` write, ever.** That is the whole "no hit event" rule: no
  damage, no crit roll, no stack accrual, no `TargetCompanion` replay.
- `CatalystKey(Entity)` reuses `ProjectileHitEmission.TargetKey`'s hash so
  catalyst and proxy gate ids share one space; distinct entity indices make
  that safe.
- `EnqueueTriggerSpawn` resolves the spawn position as the catalyst position and
  the aim from `entry.Aim`:
  `OutwardFromOwner` → catalyst position minus owner center, normalized
  (owner center recovered from the body's own position and motion data, or
  passed in the snapshot entry if cheaper);
  `OrbitTangent` → the body's velocity direction;
  `IncomingProjectile` → the projectile's travel direction.
  Degenerate vectors fall back to `(1, 0)`, matching existing helpers.
- Deterministic ids: `HashId(entry.CatalystId, entry.TypeId, projectileKey, salt)`
  with **new** salts distinct from `ImpactAoeIdSalt`, `ImpactProjectileIdSalt`,
  `ProjectileBurstIdSalt`, and `AoeOnHitAoeIdSalt`, so catalyst-triggered spawn
  ids cannot collide with impact or burst ids.

`Assets/Scripts/System/Projectiles/ProjectileDiscreteCollisionSystem.cs`
- Job takes the catalyst arrays/maps, `MaxCatalystRadius`, and `CatalystCount`.
- After the existing unit cell walk, and only when `CatalystCount > 0` and the
  projectile is still alive, call `ScanProjectile`. Unit-then-catalyst order is
  deliberate and documented, so pierce exhaustion resolves against units first.
- Fold the collision handle into `ConsumerHandle` as today; the catalyst lane
  lives in the same singleton, so no extra publication is needed.

`Assets/Scripts/System/Projectiles/ProjectileContinuousCollisionSystem.cs`
- Catalysts join the existing `FixedList512Bytes<HitCandidate>` time-of-impact
  list so units and catalysts resolve strictly nearest-first. Encode catalyst
  entries as `~index` in the existing `int TargetIndex` field — `HitCandidate`
  stays 8 bytes and candidate capacity is unchanged. Resolution branches on the
  sign to pick the array and the outcome (hit event vs spawn-only).
- Sweep geometry is the existing corridor; catalysts are tested with the same
  `CombatSweepMath` path as units.

## Acceptance criteria

- A pierce-0 projectile crossing one catalyst fires its trigger once and then
  deactivates. Nothing takes damage; no `CombatTickResult` mentions the catalyst.
- A pierce-2 projectile crossing three catalysts fires three triggers and dies
  on the third; a pierce-2 projectile crossing two catalysts continues to a
  fourth unit target beyond them.
- A projectile resting inside a catalyst fires once per
  `RepeatHitCooldownSeconds`, not once per frame.
- An enemy-faction projectile crossing a player catalyst fires nothing and
  spends no pierce.
- A catalyst with no authored trigger (`OnHitSpawn` disabled) still gates and
  still spends pierce, and emits no spawn.
- Continuous projectiles resolve a unit in front of a catalyst before the
  catalyst behind it.
- Trigger spawn ids are stable across runs for the same inputs and never equal
  an impact-spawn id for the same source ids.

## Tests to run

PlayMode, `CatalystProjectileHitTests`
- `PierceZero_FiresTriggerOnceThenDies`
- `Pierce_SpentPerCatalystAndContinuesToUnits`
- `NoCombatHitEvent_ForCatalystOverlap`
- `RestingProjectile_FiresOncePerRepeatCooldown`
- `EnemyProjectile_IgnoresFriendlyCatalyst`
- `TriggerlessCatalyst_GatesAndSpendsPierceOnly`
- `Continuous_ResolvesNearestFirstAcrossUnitsAndCatalysts`

EditMode, `CatalystHitEmissionTests`
- `TriggerSpawnId_IsDeterministic`
- `TriggerSpawnId_DoesNotCollideWithImpactSalts`
- `TriggerAim_ResolvesPerAuthoredMode`

## Dependencies

004.

## Scope

Large. This is the task that touches the two hottest jobs in the runtime;
review the added branches for register pressure and keep everything behind the
`CatalystCount > 0` gate.

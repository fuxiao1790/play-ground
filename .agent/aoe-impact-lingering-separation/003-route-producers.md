# 003 — Route producers by Kind into the two lanes

## Goal
Each producer that emits an AOE spawn event now holds a `Kind` that is already `ImpactAoe`
or `LingeringAoe` (from task 002). Give every producer **two** AOE writers/buffers (impact +
lingering) and switch on `Kind` to pick one — mirroring how producers already choose
projectile vs aoe.

Depends on: 001, 002.

## Producer sites
1. **`AoeCollisionCore.EmitHit`** ([AoeCollisionCore.cs:231](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs#L231))
   — replace the single `aoeEventWriter` + `hasAoeEventWriter` with impact + lingering
   writers; branch on `hitSpawn.OnHitSpawn.Kind`. Emit the matching event struct. Both
   `LingeringAoeCollisionSystem` and `ImpactAoeCollisionSystem` pass the two writers through
   (they fetch them from the two expansion systems — task 004).
2. **`ProjectileCollisionSystem`** ([ProjectileCollisionSystem.cs:267-278](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L267))
   — the on-impact-AOE branch: switch on `OnHitSpawn.Kind`, enqueue impact vs lingering event.
3. **`TimedSpawnSystem`** ([TimedSpawnSystem.cs:86-101](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs#L86))
   — `spawn.ChildKind` now distinguishes the two AOE variants; enqueue to the matching queue.
   The job gains a second AOE `ParallelWriter` + `Has*` flag.
4. **`StatusProcessSystem.BuildDetonationSpawn`** ([StatusProcessSystem.cs:182-199](../../Assets/Scripts/System/Status/StatusProcessSystem.cs#L182))
   — `switch (snapshot.Kind)`: split the `Aoe` case into `ImpactAoe`/`LingeringAoe`, each to
   its writer.
5. **`CombatApplyFinalizeSystem` / `CombatApplyFinalizeSingleSystem`** detonation emit
   ([CombatApplyFinalizeSingleSystem.cs:334](../../Assets/Scripts/System/Common/CombatApplyFinalizeSingleSystem.cs#L334),
   [CombatApplyFinalizeSystem.cs:378](../../Assets/Scripts/System/Common/CombatApplyFinalizeSystem.cs#L378))
   — same split by `DetonationKind`.
6. **`CombatRoot` main-thread scope appends** ([CombatRoot.cs:260](../../Assets/Scripts/System/Common/CombatRoot.cs#L260),
   [CombatRoot.cs:364](../../Assets/Scripts/System/Common/CombatRoot.cs#L364)) — append to the
   `ImpactAoeSpawnEvent` or `LingeringAoeSpawnEvent` scope buffer by variant (decided in task 002).
   Also update the counter read at [CombatRoot.cs:698](../../Assets/Scripts/System/Common/CombatRoot.cs#L698)
   to sum both buffers.

## Writer plumbing
- Producers currently fetch the AOE queue + `ProducerHandle` from `AoeSpawnExpansionSystem`.
  After task 004 they fetch from `ImpactAoeSpawnExpansionSystem` and
  `LingeringAoeSpawnExpansionSystem` (two `GetExistingSystemManaged` calls, two writers, two
  `ProducerHandle` chains). Keep this task's producer edits consistent with 004's system names.
- Preserve the existing `Has*Writer` null-guards and `ProducerHandle` combine idiom exactly.

## Acceptance criteria
- No producer emits a variant-agnostic AOE event; each routes by `Kind`.
- VFX/hit/event producer-handle chaining preserved for both lanes.
- Behavior parity: an impact-lifetime template still lands in the impact lane and vice versa
  (verified by the retargeted tests in task 006).

## Scope: medium–large (six producer sites, +1 writer each).

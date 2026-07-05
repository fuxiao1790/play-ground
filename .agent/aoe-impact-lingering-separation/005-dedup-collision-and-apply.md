# 005 — Dedup collision + apply scheduling (optional cleanup)

## Goal
Collision and apply are already correctly *separate* (each two systems over a shared core),
but the two systems in each pair duplicate their `OnUpdate` scheduling scaffolding verbatim.
This task removes that copy-paste so all three stages read as the same "thin shell" pattern.
Purely structural; no behavior change. Independent of tasks 001–004.

## Collision
`ImpactAoeCollisionSystem` and `LingeringAoeCollisionSystem` share ~55 identical lines:
fetch hash singleton, fetch producer systems, build the job, `ScheduleParallel`, then combine
`collisionHandle` into every producer's `ProducerHandle` + the hash `ConsumerHandle`
([LingeringAoeCollisionSystem.cs:41-91](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs#L41)
vs [ImpactAoeCollisionSystem.cs:37-91](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs#L37)).
- Extract a static `AoeCollisionScheduling` helper that takes the query + a delegate/struct to
  build+schedule the variant job and does all the handle wiring. Each system keeps only its
  query definition and its variant job type (`BufferGate` vs `ScratchGate`, lifetime
  present/none). After task 004 the helper also fans the collision handle into **both**
  expansion systems' `ProducerHandle`.

## Apply
`ImpactAoeSpawnApplySystem` and `LingeringAoeSpawnApplySystem` share the `OnUpdate`
reuse-then-cold-create dance and profiler wiring
([AoeSpawnApplySystem.cs:55-136](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L55)
vs [AoeSpawnApplySystem.cs:269-355](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L269)).
- Extract the shared scaffold (fetch commands from the matching expansion system, run the
  reuse job, cold-create the remainder via ECB, update counters/`CombatStatsSingleton`).
  Each system keeps only its archetype, dead-slot query, and per-entity write job
  (`ImpactAoeSpawnJob` vs `LingeringAoeSpawnJob`, which already delegate to
  `AoeSpawnApplyUtility`).

## Acceptance criteria
- No verbatim-duplicated scheduling block between the two collision systems or the two apply
  systems; the variant-specific parts (query, gate/archetype, job body) remain per-system.
- Behavior identical (same tests green).

## Notes
- Watch Burst/generics: the shared collision helper must stay Burst-friendly. If a delegate
  boundary would break Burst, prefer a small generic method constrained over the job type, or
  a shared static that returns the scheduled `JobHandle` for the caller to wire — whichever
  keeps the job Burst-compiled.

## Scope: medium. Optional — land after the core split (001–004) is green, or skip if the
duplication is judged acceptable.

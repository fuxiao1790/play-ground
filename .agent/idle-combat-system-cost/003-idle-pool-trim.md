# 003 — Trim the disabled projectile/AOE pool after busy scenes

## Problem

Projectile/AOE entities are never destroyed during churn — despawn only disables
`Active`/`CombatRenderActiveTag`/`*CollisionActiveTag`. The resident pool stays
at its busy-scene high-water mark forever. Because the reuse pipeline packs new
active entities into recently-disabled chunks, active and disabled entities stay
interleaved, so enableable chunk-skip rarely fires and every query, gather, and
`ScheduleParallel` keeps iterating the full resident set. This is the underlying
reason idle cost stays high "after a busy scene clears up." Guards (001/002)
stop per-frame waste when *fully* idle, but a few active entities keep the whole
oversized pool warm; only trimming restores cheap steady state.

## Change

Add a dedicated maintenance system (e.g. `CombatPoolTrimSystem`,
`SimulationSystemGroup`, runs at a low cadence) that destroys excess disabled
projectile/AOE entities down to a reserve floor, with hysteresis to avoid churn.

Design:

1. Two queries with `WithDisabled<Active>()`:
   - `WithAll<ProjectileTag>().WithDisabled<Active>()`
   - `WithAll<AoeTag>().WithDisabled<Active>()`
   (Also gate on the corresponding active-count queries.)
2. Cadence: only evaluate every N seconds (accumulate `SystemAPI.Time.DeltaTime`)
   or every K frames — not every frame. Structural destroys cause a sync point;
   keep them rare.
3. Trim policy with hysteresis per domain:
   - `reserveFloor` (config constant, e.g. 256) — never trim below this.
   - Let `disabled = disabled count`, `active = active count`.
   - Only trim when `disabled > max(reserveFloor, active * growthFactor) *
     hysteresisHigh` (e.g. growthFactor 2, hysteresisHigh 1.5).
   - Target retained = `max(reserveFloor, active * growthFactor)`.
   - Destroy `disabled - targetRetained` disabled entities.
4. Destruction: gather the surplus disabled entities (`ToEntityArray`, capped at
   the surplus count) and `EntityManager.DestroyEntity(NativeArray)` on the main
   thread (chunk-level batch). Complete relevant dependencies first
   (`CompleteDependency` / the combat producer handles) so no scheduled job
   references the destroyed entities.

## Notes / caveats

- **Sync-point discipline**: destroys are structural and must run at a clean
  point. Place the system where no in-flight combat jobs reference these
  entities (after collision/finalize, before next-frame spawn apply), and
  complete dependencies before destroying. Cross-check
  `Docs/simulation/ecs-notes.md` "Structural Changes & High Churn".
- **Reuse interaction**: the spawn-apply reuse path queries disabled slots to
  reuse them. Trimming reduces the reserve of reusable slots, so set
  `reserveFloor`/`growthFactor` high enough that normal bursts still reuse rather
  than cold-create. Tune against a stress scene.
- **Per-bucket fairness**: reuse is keyed by faction/render-type/slot-kind (and
  AOE faction/type id). Trimming uniformly is fine, but if certain types are hot
  and others cold, consider trimming per shared-component bucket so a hot type
  keeps its reserve. Start uniform; refine only if profiling shows cold-create
  spikes after trims.
- Expose counters (trimmed-per-trim, resident disabled per domain) so the effect
  is observable, consistent with the project's counter conventions.

## Acceptance criteria

- After a busy→clear transition, resident disabled projectile/AOE entity count
  falls back toward `reserveFloor` within a few trim cadences.
- Idle steady-state frame time after a busy scene matches a cold idle scene
  (no high-water-mark penalty).
- A sustained busy scene does not thrash (no repeated trim→cold-create cycles);
  reuse rate stays high.
- No destroyed entity is referenced by an in-flight job (no safety errors).

## Dependencies

Independent of 001–002, but most valuable combined with them: guards remove
fully-idle waste, trimming removes the residual-pool tax when a few entities
remain active.

## Scope

Medium — new system plus tuning. The structural-change timing and reuse-reserve
tuning are the risk areas; validate with a stress scene and the PlayMode combat
tests.

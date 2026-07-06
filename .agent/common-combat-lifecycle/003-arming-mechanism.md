# 003 — Arming mechanism (pause overlay)

## Goal

Add the full arming mechanism so an entity spawned with `ArmSeconds > 0` is frozen
and telegraphed until the timer elapses, then resumes as a normal armed entity.
Behavior-identical when `ArmSeconds == 0`. Testable in isolation by driving
`ArmSeconds` through a spawn command.

## New components (in `System.Common`)

- `ArmingTag : IComponentData, IEnableableComponent` — enabled iff arming. The
  single pause switch.
- `CombatArmingComponent { float Remaining }` — plain data, arm countdown.
- **Two components on purpose**: one enableable-with-data would force the arming
  job to take it as both `ref` (Remaining) and `EnabledRefRW` (disable) — the
  documented UB pattern (`reference_ijob_ref_plus_enabledref`).

## Command field

- Add `public float ArmSeconds;` to
  [ProjectileSpawnCommand](../../Assets/Scripts/System/Projectile/ProjectileSpawnPipeline.cs)
  and [AoeSpawnCommand](../../Assets/Scripts/System/Aoe/AoeSpawnPipeline.cs). Plain
  `float`, safe in the template registry.

## Archetypes + spawn apply

Add `ArmingTag` + `CombatArmingComponent` to all three combat archetypes
(`ProjectileSpawnApplySystem._archetype`, `_impactArchetype`,
`_lingeringArchetype`). In every spawn path (reuse job masks + cold-create ECB, all
archetypes):

- Set the **armed** gate values exactly as today (unchanged — `SpawnStateFor` /
  task-001 helper still authoritative).
- Additionally set `CombatArmingComponent.Remaining = cmd.ArmSeconds` and enable
  `ArmingTag` iff `cmd.ArmSeconds > 0f` (else disabled).
- If `ArmSeconds > 0`, enqueue telegraph VFX `Trigger = 4` (area from authoring
  visual scale / AOE area), co-located with the existing spawn-VFX emission. Update
  the `VfxPendingSpawn.Trigger` comment to `0=spawn 1=hit 2=expire 3=pulse 4=arming`.

## `CombatArmingSystem` (new, common)

- `[UpdateInGroup(SimulationSystemGroup)]`, `[UpdateBefore(typeof(CombatLifetimeSystem))]`
  so it clears `ArmingTag` before every held system runs (lifetime already sits
  before movement/collision/timed/tracking).
- Query `WithAll<Active, ArmingTag>` (enabled), `ref CombatArmingComponent`,
  `EnabledRefRW<ArmingTag>`. Decrement `Remaining -= DeltaTime`; when `<= 0`, clamp
  to 0 and disable `ArmingTag`. (Never reads `ArmingTag` as data — no UB.)

## Hold consumers with `WithDisabled<ArmingTag>`

Add the clause (via `[WithDisabled(typeof(ArmingTag))]` on the `IJobEntity`, or the
query builder for `SystemBase`):

- [ProjectileMovementSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileMovementSystem.cs)
  — `ProjectileMovementJob`.
- [CombatLifetimeSystem.cs](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs)
  — both `ProjectileLifetimeJob` and `AoeLifetimeJob` (arm time must not consume
  lifetime).
- [ProjectileCollisionSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs)
  — `ProjectileCollisionJob`. (The `activeProjectileQuery` `IsEmpty` early-out may
  stay conservative.)
- [ImpactAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/ImpactAoeCollisionSystem.cs)
  and [LingeringAoeCollisionSystem.cs](../../Assets/Scripts/System/Aoe/LingeringAoeCollisionSystem.cs)
  — their collision jobs.
- [TimedSpawnSystem.cs](../../Assets/Scripts/System/Common/TimedSpawnSystem.cs)
  — `TimedSpawnJob`.
- [ProjectileTrackingSystem.cs](../../Assets/Scripts/System/Projectile/ProjectileTrackingSystem.cs)
  — tracking job.

## Render suppression during arming

[CombatRenderPrepareSystem.cs](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs)
runs under `IgnoreComponentEnabledState`, so it cannot use a query clause. Add an
`ArmingTag` handle + mask to `RenderPrepareJob`; make visible iff
`activeMask[i] && !armingMask[i]` (otherwise degenerate). `Active` mask already
present from the consolidation change.

## Kill helper (if task 002 landed)

Have the shared `Kill` helper also disable `ArmingTag`. Not required for
correctness (arming system + consumers require `Active`, so a stale bit on a dead
slot is inert and reset on reuse), but keeps dead slots clean.

## Acceptance criteria

- `ArmSeconds == 0`: existing PlayMode suite passes unchanged.
- New PlayMode test: spawn a projectile and an AOE with `ArmSeconds > 0` via a
  command/event, then step frames:
  - during arming: position unchanged (frozen), target not hit (no collision), not
    rendered (degenerate matrix), `Active` enabled (slot not reused), lifetime
    `Remaining` unchanged, no timed children emitted;
  - after `Remaining` elapses: `ArmingTag` disabled, and the entity moves /
    collides / renders / counts lifetime / emits timed children normally.
- Impact AOE with `ArmSeconds > 0`: telegraphs, then performs its single collision
  pass on the armed frame.

## Scope

Medium–large, but each edit is a one-line query clause, an archetype addition, or
the small new system. No gameplay-math change.

## Dependencies

Independent of 001 and 002 (see index). 004 depends on this task's `ArmSeconds`
field.

# 002 — Two archetypes + spawn routing

**Depends on:** 001. **Scope:** `AoeSpawnApplySystem`. **Complexity:** high.

## Goal

Introduce the impact archetype (omits `CombatLifetimeComponent`, `AoeContactGateElement`,
`AoePulseVfxComponent`) alongside the lingering archetype, and route every spawn command
to the matching archetype, dead-slot pool, and reset path by `cmd.Lifetime > 0f`.

## Changes (`AoeSpawnApplySystem`)

### Archetypes ([OnCreate, :41-59](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L41))
- `lingeringArchetype` = the current set (unchanged).
- `impactArchetype` = the current set **minus** `CombatLifetimeComponent`,
  `AoeContactGateElement`, `AoePulseVfxComponent`.

### Bucket key ([AoeSpawnKey, :396](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L396))
- Add a `bool Lingering` (or third int) to `AoeSpawnKey`; include it in `Equals`/`GetHashCode`.
- Bucketing in `OnUpdate` ([:93](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L93)):
  key = `(faction, typeId, lingering: cmd.Lifetime > 0f)`.

### Dead-slot queries ([DeadSlotQueryFor, :204](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L204))
- Build two cached queries (keyed including the lingering bit): lingering adds
  `WithAll<CombatLifetimeComponent>`, impact adds `WithNone<CombatLifetimeComponent>`.
  Both keep `WithDisabled<Active>` + the `(CombatRenderFaction, CombatRenderTypeId)`
  shared-component filter. **No new tag.**

### Reuse spawn job ([AoeSpawnJob, :298](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L298))
- Impact variant must not touch the absent handles. Either:
  - a second `IJobChunk` without `LifetimeHandle` / `ContactGateHandle` / `PulseVfxHandle`
    (and no `gates[i].Clear()`, no `lifetimeMask[i]` write), or
  - guard those writes behind a `bool HasLingeringComponents` flag on one shared job.
- Lingering variant unchanged (still `gates[i].Clear()`, `lifetimeMask[i] = true`).

### Cold-create reset ([CreateAoeEntity / RecordAoeReset, :233-259](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L233))
- Pick archetype by lingering bit.
- Impact reset skips `CombatLifetimeComponent`, `AoePulseVfxComponent`, and the contact
  gate; **skips** `SetComponentEnabled<CombatLifetimeComponent>`. Keep `Active`,
  `AoeCollisionActiveTag` (`NeedsCollision`), `CombatRenderActiveTag`.
- Lingering reset sets lifetime (always enabled, `cmd.Lifetime > 0f`) + pulse vfx as today.

## Notes / pitfalls

- The reuse jobs iterate per-bucket already; the bucket key now guarantees a bucket maps to
  exactly one archetype, so each reuse job runs against the correct dead-slot pool.
- Constraint 2: impact AOEs are deactivated by the impact collision variant (Task 001), not
  the lifetime system — nothing to add here, but do not re-enable a lifetime path for them.

## Acceptance

- Spawning an AOE with `Lifetime <= 0` creates/reuses an impact-archetype entity with no
  gate buffer; `Lifetime > 0` yields a lingering entity with the buffer enabled.
- Reuse pools do not cross-claim (an impact command never reuses a lingering slot or vice
  versa); cold-create not regressed for either.
- Impact entities collide (via Task 001's impact variant) and deactivate same tick.

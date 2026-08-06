# 014 — PlayMode integration tests

**Depends on:** 012, 013. **Scope:** medium. **Risk:** low.

## Why

Unit coverage lives in each implementation task. This task proves the feature end to end in a real
scene.

## Integration tests

`Assets/Tests/PlayMode/TargetedSkillPlayModeTests.cs`

Test fixtures and scenes belong under `Assets/Tests/`, never in runtime folders.

1. **Basic cast** — player casts a targeted skill, a mob loses health, and **no projectile or AOE
   entity is created**. The negative half is the point: it proves the domain is really physics-free.
2. **Chain across three mobs** — one cast, all three lose health, damage decreasing by falloff.
3. **Staggered chain** — `chainDelaySeconds > 0`, mobs take damage in sequence across frames, not
   all at once.
4. **Interval chain** — `lifetimeSeconds > 0` ticks for its duration, and consecutive ticks do not
   open on the same mob when another is in range.
5. **Mana gate** — insufficient mana rejects the cast and refunds the cooldown.
6. **On-impact trigger** — a projectile hits a mob and a chain fires from the impact point. Assert
   the chain's first hit lands **one frame after** the projectile's, per the spawn timing rule —
   this is expected, not a defect, and the test documents it.
7. **Interval trigger** — a lingering AOE accrues energy and spawns targeted children.
8. **Stack applicator** — a chain wired through `StackTrigger` accrues stacks on every linked mob
   and detonates at threshold.
9. **Link VFX dispatch** — dispatched `LineSegment` count summed over a walk equals the resolved
   link count.
10. **Faction symmetry** — a mob-cast chain damages the player and never other mobs.
11. **Pool stability** — 200 casts leave targeted entity count bounded; slots are reused rather
    than the pool growing without limit. Run both variants.
12. **Mixed-scene pool cleanup** — projectiles, AOEs, and chains churning together: the calm-down
    gate still trims correctly, proving the stats wiring from task 005 did not skew it.

## Deferred — scene-level stress ceiling

Not part of this task. Per-resolve caps bound one instance, but nothing bounds how many chains walk
at once; the worst case the feature allows is a projectile volley carrying a
`TargetedIntervalSpawnTrigger` whose child has `count > 1`. Deferred by decision. If it ever needs
answering, the shape is a stress scene measuring concurrent instances, resolve job time, link VFX
per frame, and frame time — and the fix would be a scene-level concurrent-instance cap, which is
additive and changes no contract in this plan.

## Acceptance criteria

- All twelve integration tests pass.
- No new EditMode or PlayMode test regressions elsewhere.

## Notes

Tests must prove gameplay behaviour, not that code ran, and must not rely on production-only flags,
counters, or breadcrumbs added for testing. Observe through public runtime APIs, real effects
(damage, despawn, entity counts), and test-owned listeners. `CombatStatsDisplaySingleton` is a
legitimate observation point — it is a real diagnostic surface, not a test hook.

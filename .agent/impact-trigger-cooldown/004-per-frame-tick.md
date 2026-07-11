# 004 — Per-frame decrement of the cooldown

Without this, `OnHitSpawnCooldownRemaining` is set on emit (task 003) but never
returns to 0, so a cooldown'd source fires its impact spawn once and then never
again. Both tick sites reuse the existing `CooldownRemaining -= DeltaTime` idiom
and clamp at 0.

## Projectile — `ProjectileContactGateSystem.ProjectileContactGateJob`

This job already ticks per-projectile hit cooldowns every frame,
`[UpdateBefore(ProjectileCollisionSystem)]`. Extend it:

- Add `ref ProjectileHitComponent projectileHit` to the `Execute` signature
  (every active projectile has this component).
- Decrement, clamped:
  ```csharp
  projectileHit.OnHitSpawnCooldownRemaining =
      math.max(0f, projectileHit.OnHitSpawnCooldownRemaining - DeltaTime);
  ```
- Keep the existing contact-gate buffer passes as-is. Ensure the job's
  `[WithAll]` / query still resolves (add `ProjectileHitComponent` if an explicit
  query is used; this job uses the implicit IJobEntity query, so the added `ref`
  param joins the query automatically).

## AOE — `LingeringAoeCollisionSystem.LingeringAoeCollisionJob.Execute`

The job runs every frame for every lingering AOE and only *early-returns* after
the `hitGate.Remaining -= DeltaTime` line. Decrement the on-hit-spawn cooldown at
the **very top**, before the `hitGate.Remaining` check, so it ticks every frame
regardless of the tick gate:

```csharp
hitSpawn.OnHitSpawnCooldownRemaining =
    math.max(0f, hitSpawn.OnHitSpawnCooldownRemaining - DeltaTime);

hitGate.Remaining -= DeltaTime;
if (hitGate.Remaining > 0f) return;
...
```

(`hitSpawn` is already `ref` from task 003; `math` via `Unity.Mathematics`.)

Impact (pulse) AOEs need no tick — they live one pass.

## Acceptance criteria (user-run PlayMode)

- A stationary lingering AOE with an on-hit impact spawn and `cooldownSeconds = T`
  over enemies it hits every tick: count the spawned impact effects over a fixed
  duration `D`; expect `~floor(D / T) + 1`, not one-per-tick and not exactly one.
- A piercing projectile with impact-AOE cooldown that survives long enough to hit
  a second enemy after `T` elapses: it spawns a **second** impact AOE (proves the
  countdown returns to 0), and does **not** before `T`.
- `cooldownSeconds = 0` path unaffected.

## Dependencies

- Task 003 (sets the countdown that this decrements). Land together.

## Scope

Small. Two decrement lines in two existing every-frame jobs.

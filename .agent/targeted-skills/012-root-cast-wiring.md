# 012 — Root cast wiring

**Depends on:** 009. **Scope:** small. **Risk:** low.

## Why

Makes a targeted set castable from player input. The one interesting bit is that a root cast is the
only producer where origin and acquisition anchor differ.

## Changes

`Assets/Scripts/Skills/SkillSpawnTranslator.cs`

- Add a `RuntimeTargetedDefinition` branch beside the projectile and AOE branches:

  ```csharp
  if (def is RuntimeTargetedDefinition targeted)
  {
      if (targeted.TypeId < 0) return;
      combatRoot.SpawnRegisteredTargeted(
          targeted.SpawnTemplateKey,
          origin,                                   // caster position — segment 0 starts here
          aimWorldPos,                              // cursor — link 0 searches here
          Mathf.Max(1, targeted.Count),
          faction,
          TargetedVariant.ChildKindFor(targeted.LifetimeSeconds),
          caster,
          Mathf.Max(0f, targeted.ManaCost),
          castToken);
  }
  ```

- `SkillSpawnTranslator` already receives `origin`, `aimDir`, and `aimWorldPos` on every cast, so
  both values exist at the managed boundary today — no new plumbing (requirements §3.1,
  decision 10). Projectiles use `origin` + `aimDir`; AOEs use `aimWorldPos`; targeted is the only
  one that needs both.
- The anchor is captured **at cast time** and does not follow the cursor afterwards.

`Assets/Scripts/Skills/SkillDriver.cs`

- Include targeted definitions in the recursive registration walk (shared with task 009) so root
  and triggered targeted sets both register their templates.
- Cooldown, `SkillSlotState`, and rejection refund need no targeted-specific code — they operate on
  `RuntimeSkillDefinition.RecoveryTime` and the cast token, both of which targeted definitions
  already carry.

## Acceptance criteria

- EditMode: casting a compiled targeted root submits an `ExternalSpawnRequest` whose `Position` is
  the caster position and whose `AcquireAnchor` is the aim world position — **and they differ** when
  the cursor is not on the caster.
- EditMode: `LifetimeSeconds > 0` routes the request to `IntervalChildKind.LingeringTargeted`;
  `== 0` routes to `Targeted`.
- EditMode: a rejected cast (insufficient mana) refunds the slot cooldown through the existing
  rejection bridge.
- EditMode: a cast that acquires **no** target still spends mana and burns the cooldown
  (requirements decision 1 — matches every other root cast).
- EditMode: `Count > 1` passes the count through to the spawn request.
- EditMode: a targeted definition with `TypeId < 0` (unregistered) submits nothing and does not
  throw.

## Notes

Origin/anchor divergence is the whole reason `SpawnRegisteredTargeted` takes two positions
(task 007). Every internal producer — impact triggers, interval triggers — passes the same point
for both, so the two collapse everywhere except here.

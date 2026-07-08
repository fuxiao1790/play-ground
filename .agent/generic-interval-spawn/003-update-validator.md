---
name: update-validator
description: Collapse SkillLoadoutValidator's IsIntervalSpawnTrigger check to the merged trigger type
---

# 003 — Update validator

## Scope

`Assets/Scripts/Skills/SkillLoadoutValidator.cs`

## Changes

1. [SkillLoadoutValidator.cs:194-195](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L194-L195):
   ```csharp
   private static bool IsIntervalSpawnTrigger(TriggerLink link) =>
       link is ProjectileIntervalSpawnTrigger or AoeIntervalSpawnTrigger;
   ```
   becomes:
   ```csharp
   private static bool IsIntervalSpawnTrigger(TriggerLink link) =>
       link is IntervalSpawnTrigger;
   ```
   (Or inline `slot.link is IntervalSpawnTrigger` at the one call site
   [:139](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L139) and delete
   the helper — either is fine; keep the helper if it reads more clearly
   at the call site, since it already documents intent.)

## No other changes needed

- The `TargetSkillTags` mismatch warning at
  [:148-152](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L148-L152)
  is driven purely by `slot.link.TargetSkillTags`, which is now
  `Projectile | Aoe` on the merged trigger — it will simply stop firing for
  interval-spawn triggers (any Projectile or AOE target now matches), which
  is the intended behavior change.
- `ValidateIntervalSpawnSource` (the pulse-AOE-source warning,
  [:181-192](../../Assets/Scripts/Skills/SkillLoadoutValidator.cs#L181-L192))
  is untouched — still gated by `IsIntervalSpawnTrigger` and still checks the
  *source* skill, which this task does not change.

## Acceptance Criteria

- Placing an `IntervalSpawnTrigger` between a Projectile source and an AOE
  target (or vice versa) produces zero `UnsupportedTriggerTarget` /
  `UnsupportedTriggerSource` warnings (target-tag check no longer fires for
  this trigger).
- Placing an `IntervalSpawnTrigger` on a pulse-AOE source still produces the
  existing "Pulse AOEs have no duration to tick" warning.

## Dependencies

Depends on [001-merge-trigger-type.md](./001-merge-trigger-type.md).

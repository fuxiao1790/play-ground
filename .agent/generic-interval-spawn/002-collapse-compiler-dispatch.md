---
name: collapse-compiler-dispatch
description: Merge SkillSetCompiler's two interval-spawn Apply methods into one dispatched by compiled child type
---

# 002 — Collapse compiler dispatch

## Scope

`Assets/Scripts/Skills/SkillSetCompiler.cs`

## Changes

1. In `Compile` ([SkillSetCompiler.cs:46-56](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L46-L56)),
   replace the two separate branches:
   ```csharp
   if (chain.link is ProjectileIntervalSpawnTrigger childTrigger)
   {
       ApplyChildSpawn(triggerHost, childTrigger, slots, chain.effectIndex, allChains, snapshot);
       continue;
   }

   if (chain.link is AoeIntervalSpawnTrigger aoeIntervalTrigger)
   {
       ApplyAoeIntervalSpawn(triggerHost, aoeIntervalTrigger, slots, chain.effectIndex, allChains, snapshot);
       continue;
   }
   ```
   with one:
   ```csharp
   if (chain.link is IntervalSpawnTrigger intervalTrigger)
   {
       ApplyIntervalSpawn(triggerHost, intervalTrigger, slots, chain.effectIndex, allChains, snapshot);
       continue;
   }
   ```

2. Replace `ApplyChildSpawn` ([:298-332](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L298-L332))
   and `ApplyAoeIntervalSpawn` ([:334-366](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L334-L366))
   with one `ApplyIntervalSpawn`:
   ```csharp
   private static void ApplyIntervalSpawn(
       RuntimeSkillDefinition parent,
       IntervalSpawnTrigger trigger,
       IReadOnlyList<LoadoutSlot> slots,
       int effectIndex,
       TriggerChain[] allChains,
       PlayerStatSnapshot snapshot)
   {
       if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
           return;

       if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
           return;

       RuntimeSkillDefinition compiledChild = Compile(slots, effectIndex, allChains, snapshot);
       float intervalSeconds = Mathf.Max(0.01f, trigger.intervalSeconds);
       float intervalJitterSeconds =
           intervalSeconds * Mathf.Clamp(trigger.intervalJitterPercent, 0f, 100f) * 0.01f;

       if (compiledChild is RuntimeProjectileDefinition projectileChild)
       {
           var setup = new RuntimeChildSpawnSetup
           {
               JitterSeed = ++nextChildJitterSeed,
               ChildDefinition = projectileChild,
               IntervalSeconds = intervalSeconds,
               IntervalJitterSeconds = intervalJitterSeconds,
               Behavior = new ProjectileChildSpawnBehavior(
                   Mathf.Max(1, projectileChild.Count + trigger.spawnCount),
                   ProjectileChildSpawnPatternType.SideSpray,
                   trigger.sideSpreadDegrees),
           };

           if (parent is RuntimeProjectileDefinition projectileParent)
               projectileParent.ChildSpawnSetup = setup;
           else if (parent is RuntimeAoeDefinition aoeParent)
               aoeParent.ChildSpawnSetup = setup;

           return;
       }

       if (compiledChild is RuntimeAoeDefinition aoeChild)
       {
           var setup = new RuntimeAoeIntervalSpawnSetup
           {
               JitterSeed = ++nextChildJitterSeed,
               ChildDefinition = aoeChild,
               IntervalSeconds = intervalSeconds,
               IntervalJitterSeconds = intervalJitterSeconds,
               Count = Mathf.Max(1, aoeChild.EchoCount + trigger.spawnCount),
               SideSpreadDegrees = trigger.sideSpreadDegrees,
           };

           if (parent is RuntimeProjectileDefinition projectileParent)
               projectileParent.AoeIntervalSpawnSetup = setup;
           else if (parent is RuntimeAoeDefinition aoeParent)
               aoeParent.AoeIntervalSpawnSetup = setup;
       }
   }
   ```
   Notes:
   - The interval-seconds/jitter computation is now shared instead of
     duplicated (was identical in both original methods).
   - Behavior is preserved exactly: pulse-AOE guard first, then compile the
     child once, then dispatch purely on what the child compiled to (a
     `RuntimeStackingDetonation` or `null` compiled child falls through both
     `if`s as a no-op, matching the original `is not RuntimeXDefinition
     childDef => return` guards).

## Acceptance Criteria

- All 4 source/target combinations compile correctly through one
  `IntervalSpawnTrigger`:
  - Projectile source -> Projectile target -> `ChildSpawnSetup` populated.
  - Projectile source -> AOE target -> `AoeIntervalSpawnSetup` populated.
  - Lingering AOE source -> Projectile target -> `ChildSpawnSetup` populated.
  - Lingering AOE source -> AOE target -> `AoeIntervalSpawnSetup` populated.
  - Pulse AOE source -> either target -> no-op (both setups null), matching
    prior behavior.
- No other callers of `ApplyChildSpawn`/`ApplyAoeIntervalSpawn` exist (private
  methods, single call site each) — verify via project-wide search before
  deleting.

## Dependencies

Depends on [001-merge-trigger-type.md](./001-merge-trigger-type.md) landing
first (`IntervalSpawnTrigger` must exist).

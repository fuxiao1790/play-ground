# 003 — Collapse the three-way compiler dispatch into one method

## Scope

`Assets/Scripts/Skills/SkillSetCompiler.cs`

## Change

In `CompileInternal`, the existing three-way branch:

```csharp
if (link is ProjectileIntervalSpawnTrigger childTrigger)
{
    ApplyChildSpawn(triggerHost, childTrigger, nodes, targetNodeIndex, snapshot);
}
else if (link is AoeIntervalSpawnTrigger aoeIntervalTrigger)
{
    ApplyAoeIntervalSpawn(triggerHost, aoeIntervalTrigger, nodes, targetNodeIndex, snapshot);
}
else if (link is TargetedIntervalSpawnTrigger targetedIntervalTrigger)
{
    ApplyTargetedIntervalSpawn(triggerHost, targetedIntervalTrigger, nodes, targetNodeIndex, snapshot);
}
```

becomes a single branch:

```csharp
if (link is IntervalSpawnTrigger intervalTrigger)
{
    ApplyIntervalSpawn(triggerHost, intervalTrigger, nodes, targetNodeIndex, snapshot);
}
```

Replace `ApplyChildSpawn`, `ApplyAoeIntervalSpawn`, and `ApplyTargetedIntervalSpawn`
with one `ApplyIntervalSpawn` that compiles the child once and switches on its
compiled runtime type — this is the piece of dispatch logic that necessarily
changes once the trigger subtype no longer implies the target type:

```csharp
private static void ApplyIntervalSpawn(
    RuntimeSkillDefinition parent,
    IntervalSpawnTrigger trigger,
    IReadOnlyList<SkillLoadoutNode> nodes,
    int targetNodeIndex,
    SkillStatSnapshot snapshot)
{
    if (parent is not RuntimeProjectileDefinition and not RuntimeAoeDefinition)
        return;

    if (parent is RuntimeAoeDefinition { LifetimeSeconds: <= 0f })
        return;

    RuntimeSkillDefinition compiledChild = CompileInternal(
        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);

    switch (compiledChild)
    {
        case RuntimeProjectileDefinition childDef:
        {
            var setup = new RuntimeChildSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                Behavior = new ProjectileChildSpawnBehavior(
                    Mathf.Max(1, childDef.Count + trigger.projectileCount),
                    ProjectileChildSpawnPatternType.SideSpray,
                    trigger.sideSpreadDegrees),
            };
            if (parent is RuntimeProjectileDefinition p) p.ChildSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition a) a.ChildSpawnSetup = setup;
            ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
            break;
        }
        case RuntimeAoeDefinition childDef:
        {
            var setup = new RuntimeAoeIntervalSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                Count = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
                ScatterRadius = Mathf.Max(0f, trigger.scatterRadius),
            };
            if (parent is RuntimeProjectileDefinition p) p.AoeIntervalSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition a) a.AoeIntervalSpawnSetup = setup;
            ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
            break;
        }
        case RuntimeTargetedDefinition childDef:
        {
            var setup = new RuntimeTargetedIntervalSpawnSetup
            {
                JitterSeed = ++nextChildJitterSeed,
                ChildDefinition = childDef,
                EnergyPerSecond = trigger.ResolveEnergyPerSecond(snapshot),
                EnergyThreshold = trigger.ManaToEnergyCost(childDef.ManaCost),
                EchoCount = Mathf.Max(1, childDef.EchoCount + trigger.echoCount),
            };
            if (parent is RuntimeProjectileDefinition p) p.TargetedIntervalSpawnSetup = setup;
            else if (parent is RuntimeAoeDefinition a) a.TargetedIntervalSpawnSetup = setup;
            ApplyIncomingTriggerManaCostMultiplier(childDef, trigger);
            break;
        }
    }
}
```

This is a mechanical merge of the three existing method bodies — no field,
threshold, or multiplicity formula changes. The only behavioral delta is that
dispatch now keys off `compiledChild`'s actual type instead of the caller
having pre-selected a subclass, so e.g. a merged trigger with a projectile
source and an AOE effect now correctly builds `AoeIntervalSpawnSetup` instead
of silently doing nothing.

`RuntimeChildSpawnSetup`, `RuntimeAoeIntervalSpawnSetup`, and
`RuntimeTargetedIntervalSpawnSetup` (in `RuntimeProjectileDefinition.cs`) are
**not** touched — out of scope per the plan.

## Acceptance Criteria

- `SkillSetCompiler` compiles with no references to the deleted trigger
  subclasses.
- A merged `IntervalSpawnTrigger` linking a projectile/lingering-AOE source to
  a projectile effect populates `ChildSpawnSetup` (and not the other two
  setup fields).
- Same for an AOE effect → `AoeIntervalSpawnSetup`, and a targeted effect →
  `TargetedIntervalSpawnSetup`.
- Energy threshold, mana-cost-factor, and additive-count formulas are
  byte-for-byte identical to today's per-variant methods (this is a dispatch
  merge, not a formula change).

## Dependencies

Requires [002](./002-merge-trigger-class.md).

## Scope/Complexity

Medium. Single-file, mechanical merge of three known-good method bodies into
one switch; the risk is transcription errors in the formulas, not design risk.

# 001 — StackTrigger Owns Stack Config and Wrapping

**Scope:** medium. One assembly, five files edited, two deleted.
**Depends on:** nothing.
**Lands with:** 002 (same commit — the test assembly references the deleted type).

## Goal

`StackTrigger` authors the stack accrual config and builds the
`RuntimeStackingDetonation` around its compiled effect. `StackingSupport` and
`ConversionSupport` are deleted, along with every mechanism that existed only to
support the split.

## Edits

### 1. `Assets/Scripts/Skills/Trigger/StackTrigger.cs`

Add the three accrual fields as **public fields**, matching sibling trigger
convention (`IntervalSpawnTrigger.echoCount`, `OnImpactProjectileTrigger.spawnCount`),
not the private-field + property style `StackingSupport` used.

```csharp
[Min(1)] public int stackThreshold = 3;
[Min(0f)] public float debuffLifetimeSeconds = 4f;
[Min(1)] public int stacksPerHit = 1;
```

Defaults carry over `StackingSupport`'s own type defaults. The shipped
Low(10)/High(30) presets are deliberately **not** preserved — see Resolved
Decision 1. Unity applies field initializers before deserializing YAML and these
keys are absent from the existing `StackTrigger.asset`, so stacking keeps
working at default tuning from the moment this task lands.

Narrow the target tags — `BuildStackEffectSnapshot` (`SkillDriver.cs:1670-1745`)
only materializes AOE and projectile detonations:

```csharp
public override SkillDefinitionTags TargetSkillTags =>
    SkillDefinitionTags.Projectile | SkillDefinitionTags.Aoe;
```

`SourceSkillTags` stays `Any` — projectile, AOE, and targeted definitions all
have a `StackingDetonation` field.

### 2. `Assets/Scripts/Skills/SkillSetCompiler.cs`

**Delete `:54-55`** — the root `DebuffName` special case. A node's own compile
can no longer produce a `RuntimeStackingDetonation`.

**Delete `:59-66`** — the `triggerHost` variable and its comment. Replace every
`triggerHost` usage in the trigger branches with `runtime`. The invariant it
protected (I3) now holds structurally: the effect node compiles to the inner
definition, so its own outgoing trigger already attaches to the right object.

**Rewrite the `StackTrigger` branch (`:148-170`)** so the trigger builds the
wrapper instead of expecting one back:

```csharp
else if (link is StackTrigger stackTrigger)
{
    RuntimeSkillDefinition compiledTarget = CompileInternal(
        nodes, targetNodeIndex, snapshot, includeTriggeredManaCosts: false);
    if (compiledTarget != null)
    {
        var stackingDetonation = new RuntimeStackingDetonation
        {
            Detonation = compiledTarget,
            StackThreshold = Mathf.Max(1, stackTrigger.stackThreshold),
            DebuffLifetimeSeconds = Mathf.Max(0f, stackTrigger.debuffLifetimeSeconds),
            StacksPerHit = Mathf.Max(1, stackTrigger.stacksPerHit),
            DebuffName = GetSkillSet(nodes, targetNodeIndex)?.Skill?.name,
        };

        ApplyIncomingTriggerManaCostMultiplier(stackingDetonation, link);

        if (runtime is RuntimeProjectileDefinition projDef)
            projDef.StackingDetonation = stackingDetonation;
        else if (runtime is RuntimeAoeDefinition aoeDef)
            aoeDef.StackingDetonation = stackingDetonation;
        else if (runtime is RuntimeTargetedDefinition targetedDef)
            targetedDef.StackingDetonation = stackingDetonation;
    }
}
```

Ordering is load-bearing (I4): construct, then stamp the mana factor, then
attach. `Mathf.Max` clamps mirror the ones `StackingSupport.Compile` applied.

**Delete `ApplyConversionSupports` (`:284-303`)** and unwrap its call in
`CompileDefinition` (`:193-197`) to `BuildRuntime(defCopy, modifiers, snapshot)`.
`CompileDefinition`'s `definition` parameter becomes unused — drop it and update
its one call site (`:50`) if nothing else needs it.

### 3. `Assets/Scripts/Skills/SkillLoadoutCompiler.cs`

Delete `HasTriggeredOnlyConversionSupport` (`:56-72`) and its guard (`:25-26`),
leaving the positional root rule alone:

```csharp
if (!hasIncomingTrigger)
    rootNodeIndices.Add(i);
```

Keep the `RuntimeStackingDetonation` unwrap in `AppendCompilerWarnings`
(`:82-86`) — nested detonations are still reached via `.StackingDetonation`
recursion at `:112` and `:122`.

### 4. `Assets/Scripts/Skills/SkillLoadoutValidator.cs`

Delete, in this order:

- `stackTriggerEffectIndices` construction (`:57-66`) and the
  `ValidateStackingSupportReachability` call (`:72`) plus its body (`:331-345`)
- the `link is StackTrigger` early return (`:214-218`) and
  `ValidateStackTriggerTarget` (`:347-358`)
- the `HasStackingSupport(effectSet)` warning (`:220-224`) and the helper
  (`:360-376`)

`StackTrigger` then falls through to the generic tag checks (`:251-265`), which
is the intended replacement for all three deleted rules.

### 5. `Assets/Scripts/Skills/SkillValidationWarning.cs`

Remove `UnsupportedStackingDetonation` (`:12`). No remaining producer after the
validator edits; the enum is runtime-only (validator output → `SkillDriver.ValidationWarnings`),
never serialized, so re-numbering later members is safe.

### 6. Deletions

- `Assets/Scripts/Skills/Support/StackingSupport.cs` (+ `.meta`)
- `Assets/Scripts/Skills/Support/ConversionSupport.cs` (+ `.meta`)

## Acceptance criteria

- [ ] Project compiles with zero references to `StackingSupport` or
      `ConversionSupport` outside deleted files.
- [ ] `grep -r "ConvertsToTriggeredOnly\|ApplyConversionSupports\|HasStackingSupport\|UnsupportedStackingDetonation" Assets/Scripts` returns nothing.
- [ ] A loadout of `[applicator] -StackTrigger-> [detonation set]` compiles to an
      applicator whose `StackingDetonation` is a `RuntimeStackingDetonation`
      carrying the trigger's threshold/lifetime/stacksPerHit and
      `DebuffName == detonationSet.Skill.name`.
- [ ] The detonation set's own outgoing trigger (e.g. `OnAoeHitSpawn`) attaches
      to the **inner** definition, not the wrapper (I3).
- [ ] Chain mana cost for `active -link-> triggered` is unchanged from before the
      refactor for an identical loadout (I4).
- [ ] `A → StackTrigger → A → StackTrigger → A` compiles to distinct
      `RuntimeStackingDetonation` instances that each receive a distinct
      `DebuffKey` after `SkillDriver` registration (I1).
- [ ] A detonation set with no incoming trigger is now compiled as a root
      (intended change #1) and produces no validation warning.
- [ ] `StackTrigger` → targeted skill produces `UnsupportedTriggerTarget`.

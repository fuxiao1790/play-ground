# 006 — SkillDriver owner lifecycle

**Scope:** medium. **Depends on:** 002.

This is the task that actually stops the leak. Nothing unregisters today, and
`SkillDriver` is the only durable registrar.

## Leak sources

- `CompileAndRegister` → `RegisterSpawnTemplates(compiled.Warnings)`
  (`SkillDriver.cs:224`) runs on every loadout recompile. The previous compile's keys
  are dropped on the floor.
- `BindCombatRoot` → `RegisterSpawnTemplates()` (`SkillDriver.cs:136`) runs per mob.
  Mobs spawn and despawn continuously (`SpawnController.cs:111`); nothing releases on
  despawn. In a long session this dominates the respec leak.

## Change

Track the key set each registration pass produced.

```csharp
private readonly List<(IntervalChildKind Kind, Hash128 Key)> registeredTemplateKeys = new();
```

`RegisterSpawnTemplatesRecursive` and the three `Register*IntervalTemplate` helpers
already have the key at every `combatRoot.RegisterSpawnTemplate` /
`RegisterTimedSpawnTemplate` call (`SkillDriver.cs:670`, `:691`, `:764`, `:790`,
`:811`, `:827`). Append `(kind, key)` at each.

`RegisterSpawnTemplates` becomes register-new-then-release-old:

```csharp
private void RegisterSpawnTemplates(List<SkillValidationWarning> warnings = null)
{
    if (combatRoot == null || compiledSlots == null) return;

    List<(IntervalChildKind, Hash128)> previous = registeredTemplateKeys;
    registeredTemplateKeys = new List<(IntervalChildKind, Hash128)>();

    for (int i = 0; i < activeSlotCount; i++)
        RegisterSpawnTemplatesRecursive(compiledSlots[i], 1, warnings, i);

    ReleaseTemplateKeys(previous);
}
```

**Order is load-bearing.** Registering the new set first means a key shared between
the old and new compile never reaches `OwnerCount == 0`, so it is never erased and
re-added. Releasing first would churn the map and, worse, could erase an entry that
in-flight entities still reference if `InstanceCount` also happened to be 0.

`ReleaseTemplateKeys` calls `combatRoot.UnregisterSpawnTemplate(kind, key)` per entry
and clears the list.

## Teardown

Release on every path where the driver stops holding its keys:

- `OnDestroy` — mob despawn and scene teardown.
- `BindCombatRoot` when `combatRoot` is being replaced by a different root — release
  against the **old** root before rebinding. The current early-out
  (`if (combatRoot == root) return;`, `SkillDriver.cs:131`) already skips the no-op
  case.

Guard both on `combatRoot != null`. `UnregisterSpawnTemplate` is already safe against
unknown keys and a torn-down scope (002), so ordering against `CombatRoot.OnDestroy`
does not need to be guaranteed.

## Acceptance criteria

- Recompiling an unchanged loadout leaves the registry entry count unchanged.
- Recompiling a changed loadout drops the entries only the old compile used, once no
  in-flight entity carries them, and keeps the shared ones with no erase/re-add churn.
- Spawning and despawning N identical mobs returns the registry to its pre-spawn entry
  count once their entities are gone.
- A driver destroyed while its projectiles are still in flight does not break those
  projectiles' follow-up spawns — the entries survive on `InstanceCount` until the
  entities are gone.
- No duplicate release: a key registered once per pass is unregistered exactly once.

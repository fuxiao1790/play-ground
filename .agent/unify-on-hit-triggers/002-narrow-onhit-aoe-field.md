# 002 — Narrow `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` to `RuntimeAoeDefinition`

**Depends on:** [001-onhittrigger-collapse.md](001-onhittrigger-collapse.md).
**Scope:** Small. One field type, two guards, one comment.

## Goal

Make the field's declared type say what the runtime actually accepts, so the
`is RuntimeAoeDefinition` guards downstream stop compensating for a type that
was too wide.

## Why this is now possible

`RuntimeAoeDefinition.OnHitAoeSpawnDefinition` is declared as
`RuntimeSkillDefinition` (`RuntimeAoeDefinition.cs:54`) solely because the old
`OnAoeHitSpawnTrigger` branch (`SkillSetCompiler.cs:126-134`) assigned
`compiledTarget` without a type test. Every reader already narrows it back:

- `SkillDriver.cs:1017` — `if (def.OnHitAoeSpawnDefinition is RuntimeAoeDefinition onHitAoe …)`
- `SkillDriver.cs:1045` — the same guard on `RuntimeTargetedDefinition`, whose
  field is *already* `RuntimeAoeDefinition` (`RuntimeTargetedDefinition.cs:30`),
  so that guard is redundant today.

After task 001 the only writer is `AttachOnHitTarget`'s AOE-target arm, which
assigns a `RuntimeAoeDefinition`. The two sibling fields
(`OnHitProjectileSpawnDefinition`, `OnHitTargetedSpawnDefinition`) are already
narrowly typed, so this also makes the trio consistent.

## Edits

### 1. `Assets/Scripts/Skills/Runtime/RuntimeAoeDefinition.cs:52-54`

```csharp
// Compiled from OnHitTrigger with an AOE effect; null if none. Chain depth is
// bounded by SpawnTemplateLimits.MaxSpawnChainDepth.
public RuntimeAoeDefinition OnHitAoeSpawnDefinition { get; set; }
```

### 2. `Assets/Scripts/Skills/SkillDriver.cs`

- `:1017` — `if (def.OnHitAoeSpawnDefinition is RuntimeAoeDefinition onHitAoe && !IsDefault(onHitAoe.SpawnTemplateKey))`
  becomes a null check plus the key test against the field directly.
- `:1045` — same simplification in `BuildOnHitSpawnRef(RuntimeTargetedDefinition)`;
  this one is already redundant regardless of task 001.

Both bodies keep `AoeVariant.AoeChildKindFor(…LifetimeSeconds)` and the same
`OnHitSpawnRef` shape. **Do not reorder** the projectile → aoe → targeted
priority in either method — index I1 depends on it.

### 3. Verify, do not edit

These pass the field to `RuntimeSkillDefinition` parameters and keep compiling
unchanged; confirm rather than touch:

| Site | Call |
|---|---|
| `SkillDriver.cs:277` | `RegisterSoundsRecursive` |
| `SkillDriver.cs:582-583` | `RegisterProjectileTypesRecursive` |
| `SkillDriver.cs:736-737` | `RegisterSpawnTemplatesRecursive` |
| `SkillDriver.cs:1160-1161` | `RegisterAoeTypesRecursive` |
| `SkillDriver.cs:1221` | `RegisterTargetedTypesRecursive` |
| `SkillLoadoutCompiler.cs:97` | `AppendCompilerWarnings` |
| `SkillSetCompiler.cs:577` | `GetManaCostMultiplier` |
| `SkillSetCompiler.cs:632` | `SumTriggeredSkillManaCosts` |

## Acceptance criteria

1. `RuntimeAoeDefinition.OnHitAoeSpawnDefinition` is typed `RuntimeAoeDefinition`.
2. No `OnHitAoeSpawnDefinition is RuntimeAoeDefinition` pattern remains anywhere.
3. `BuildOnHitSpawnRef` still checks projectile → aoe → targeted in that order in
   both overloads.
4. Unity compiles with no new warnings; no call site needed a cast added.

## Note

If task 001 is landed without 002, nothing breaks — 002 is a shape cleanup the
first task unlocks, not a correctness fix.

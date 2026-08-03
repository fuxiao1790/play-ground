# 011 — Lane Naming: Authoring And Compile Chain

**Depends on:** none (independent of 010 — disjoint symbols)
**Scope:** small — pure rename, one serialization hazard

## Goal

Carry the `Discrete`/`Continuous` vocabulary through the nine-hop authoring chain from
`ProjectileDefinition` to `ProjectileSpawnCommand`, so an author reading the Inspector sees the
same word the simulation uses.

## Renames

| Now | New | Location |
|---|---|---|
| `ProjectileDefinition.sweptCollision` | `continuousCollision` | [SkillDefinition.cs:42](../../Assets/Scripts/Skills/SkillDefinition.cs#L42) |
| `RuntimeProjectileDefinition.SweptCollision` | `ContinuousCollision` | [RuntimeProjectileDefinition.cs:52](../../Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs#L52) |
| `ProjectileSpawnRequest.SweptCollision` (×2 types) | `ContinuousCollision` | [ProjectileSpawnRequest.cs:179](../../Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs#L179), [:321](../../Assets/Scripts/System/Projectiles/ProjectileSpawnRequest.cs#L321) |
| ctor parameters `sweptCollision` (×2) | `continuousCollision` | `:117`, `:256` |
| `ProjectileSpawnCommand.SweptCollision` | `ContinuousCollision` | [ProjectileSpawnPipeline.cs:41](../../Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs#L41) |
| `SkillValidationWarningCode.SweptProjectileCannotTrack` | `ContinuousCollisionCannotTrack` | [SkillValidationWarning.cs:14](../../Assets/Scripts/Skills/SkillValidationWarning.cs#L14) |

Call sites that follow from the above: [SkillSetCompiler.cs:305](../../Assets/Scripts/Skills/SkillSetCompiler.cs#L305)
and `:311`, [SkillDriver.cs:282](../../Assets/Scripts/Skills/SkillDriver.cs#L282) and `:1097`,
[CombatRoot.cs:486](../../Assets/Scripts/System/Core/CombatRoot.cs#L486),
[ProjectileSpawnExpansionSystem.cs:294](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L294).

`ContinuousCollisionCannotTrack` drops "Projectile" because the enum is
`SkillValidationWarningCode` and every member is already skill-scoped; the sibling
`TrackingProjectileMayTunnel` is unchanged.

The `OnValidate` message at [SkillDefinition.cs:55](../../Assets/Scripts/Skills/SkillDefinition.cs#L55)
is player-invisible but author-visible — update the string:

```csharp
Debug.LogError("Projectile definitions cannot enable both continuous collision and tracking.");
```

## The one real hazard: `sweptCollision` is a serialized field

`ProjectileDefinition` is `[Serializable]` and `sweptCollision` is a public field, so Unity
serializes it by name. Renaming a serialized field normally discards whatever authors set.

**Right now it discards nothing.** No `.asset` in the project contains `sweptCollision` — the
skill assets have not been re-serialized since the field was added, which is verifiable:

```
grep -r sweptCollision Assets --include=*.asset   # no matches
grep -r trackingEnabled Assets --include=*.asset  # 10 files
```

So the rename is free **today**, and stops being free the moment anyone opens a skill asset in
the Inspector and Unity rewrites it. Two things follow:

1. **Do this rename before authoring any continuous skill.** It is currently a zero-risk
   window and the window closes on the first Inspector save.
2. **Add the attribute anyway** — one line, and the file already uses the pattern at
   [SkillDefinition.cs:38](../../Assets/Scripts/Skills/SkillDefinition.cs#L38):

   ```csharp
   [FormerlySerializedAs("sweptCollision")]
   public bool continuousCollision;
   ```

   It costs nothing and removes the ordering dependency in (1) entirely. Safe to delete once a
   skill asset has been saved post-rename.

`SkillValidationWarningCode` needs no equivalent care: Unity serializes enums by integer value,
and the rename does not reorder members. Confirm no member is inserted or moved while renaming.

## Acceptance Criteria

- `grep -i swept` over `Assets/Scripts/Skills/` and `Assets/Scripts/System/Core/` returns
  nothing.
- `continuousCollision` carries `[FormerlySerializedAs("sweptCollision")]`.
- No `SkillValidationWarningCode` member changed position or was inserted; only
  `SweptProjectileCannotTrack` was renamed.
- The `OnValidate` error string says "continuous collision".
- Diff is identifiers and one string literal. No changed conditions, no changed control flow —
  in particular `runtime.SpawnBlocked = runtime.ContinuousCollision && runtime.Tracking.Enabled`
  is the same expression it was.

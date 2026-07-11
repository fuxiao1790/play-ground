# 001 — Authoring + compile flow

Plumb an authored `cooldownSeconds` from the two impact-trigger SOs down to
`OnHitSpawnRef.CooldownSeconds`. **No runtime behavior change yet** — this task
only carries the value; nothing reads it until task 003.

## Changes

1. **`OnImpactAoeTrigger.cs`** — add a field (the SO currently has none):
   ```csharp
   [Min(0f)] public float cooldownSeconds;
   ```
2. **`OnImpactProjectileTrigger.cs`** — add the same
   `[Min(0f)] public float cooldownSeconds;` alongside `spawnCount` / `spreadDegrees`.
3. **`RuntimeProjectileDefinition.cs` and `RuntimeAoeDefinition.cs`** — add a host
   scalar describing "the cooldown for my on-hit spawn":
   ```csharp
   // Set from OnImpactAoeTrigger / OnImpactProjectileTrigger; 0 = fire every hit.
   public float OnHitSpawnCooldownSeconds { get; set; }
   ```
4. **`IntervalChildTemplates.cs` — `OnHitSpawnRef`** — add:
   ```csharp
   public float CooldownSeconds;
   ```
   Leave `Enabled` as-is (it keys off `TemplateKey` only).
5. **`SkillSetCompiler.cs`** — in the two impact-trigger branches, copy the
   trigger's cooldown onto the host runtime def (`triggerHost`):
   - `OnImpactAoeTrigger` branch (`is OnImpactAoeTrigger` → cast to read the field):
     ```csharp
     if (chain.link is OnImpactAoeTrigger impactAoeTrigger)
     {
         ...compile target...
         if (triggerHost is RuntimeProjectileDefinition projDef)
         {
             projDef.ImpactAoeDefinition = aoeTarget;
             projDef.OnHitSpawnCooldownSeconds = Mathf.Max(0f, impactAoeTrigger.cooldownSeconds);
         }
         else if (triggerHost is RuntimeAoeDefinition sourceAoeDef)
         {
             sourceAoeDef.OnHitAoeSpawnDefinition = aoeTarget;
             sourceAoeDef.OnHitSpawnCooldownSeconds = Mathf.Max(0f, impactAoeTrigger.cooldownSeconds);
         }
     }
     ```
   - `OnImpactProjectileTrigger` branch (`impactProjTrigger` is already bound):
     set `projDef.OnHitSpawnCooldownSeconds` / `aoeSourceDef.OnHitSpawnCooldownSeconds`
     to `Mathf.Max(0f, impactProjTrigger.cooldownSeconds)` in the same place the
     `ImpactProjectileDefinition` / `OnHitProjectileSpawnDefinition` is assigned.
   - **Do not** touch `OnHitSpawnCooldownSeconds` in the `OnAoeHitSpawnTrigger`
     branch — that trigger keeps cooldown 0 (fire every hit). It shares the
     `OnHitAoeSpawnDefinition` field with the AOE-source impact-AOE branch, so the
     cooldown is what distinguishes them.
6. **`SkillDriver.cs` — both `BuildOnHitSpawnRef` overloads** — set the new field
   on the returned ref, in every branch that returns a non-default ref:
   ```csharp
   return new OnHitSpawnRef
   {
       Kind = ...,
       TemplateKey = ...,
       CooldownSeconds = Mathf.Max(0f, def.OnHitSpawnCooldownSeconds)
   };
   ```

## Acceptance criteria

- Project compiles.
- A `SkillLoadout` with `OnImpactAoe`/`OnImpactProjectile` and `cooldownSeconds > 0`
  compiles; inspect the produced `OnHitSpawnRef.CooldownSeconds` (via a compile
  test or debug log) and confirm it matches the authored value on both a
  projectile source and an AOE source.
- `cooldownSeconds = 0` leaves `OnHitSpawnRef.CooldownSeconds == 0`.
- Existing skill-validation edit-mode tests still pass.

## Scope

Small. Pure data plumbing across 6 files; no jobs touched.

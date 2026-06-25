# 004 — Drop the obsolete AOE-source "flat burst" warning

**File:** `Assets/Scripts/Skills/SkillSetCompiler.cs`
**Depends on:** 002, 003
**Scope:** trivial

## Change

In the `OnImpactProjectileTrigger` branch, the AOE-source path currently warns that the flat
`AoeProjectileBurstSnapshot` cannot carry the projectile's own impact AOE/projectile (lines 80-84):

```csharp
else if (runtime is RuntimeAoeDefinition aoeSourceDef)
{
    aoeSourceDef.OnHitProjectileSpawnDefinition = impactProjDef;
    if (impactProjDef.ImpactProjectileDefinition != null || impactProjDef.ImpactAoeDefinition != null)
    {
        ... Debug.LogWarning("... those nested chains will not fire.");
    }
}
```

Remove the inner `if` + warning (the burst now carries the impact). Keep the assignment.

Leave the **projectile-source** proj→proj→proj warning untouched — that is still a real limit
(`BuildImpactProjectileEvent` truncates the 3rd projectile level by design).

## Acceptance criteria

- No warning is logged when compiling `AOE → OnImpactProjectile → projectile (with its own trigger)`.
- The proj→proj→proj warning still fires for the projectile-source nested case.

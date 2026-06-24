# 003 — Compile-time registration walk

## Scope

Build each interval template once at compile time, register it, and store its `TemplateKey` on
the setup object. Move the template builders out of the per-cast translator path.

## Changes

1. **Setup objects** (`Assets/Scripts/Skills/Runtime/RuntimeProjectileDefinition.cs`,
   `RuntimeAoeDefinition.cs`): add `Hash128 TemplateKey` to `RuntimeChildSpawnSetup` and
   `RuntimeAoeIntervalSpawnSetup`.

2. **Registration walk** (`Assets/Scripts/Skills/PlayerSkillDriver.cs`): add
   `RegisterIntervalTemplates()` invoked from `CompileAndRegister()` **after**
   `RegisterProjectileTypes()`/`RegisterAoeTypes()` (template build reads child `TypeId`s).
   Walk the compiled slot tree (mirror `RegisterProjectileTypesRecursive`); for each
   `ChildSpawnSetup`/`AoeIntervalSpawnSetup`, build the corresponding
   `ProjectileSpawnTemplateData`/`AoeSpawnTemplateData` (using `combatRoot` for
   faction/target-mask), call `combatRoot.RegisterTimedSpawnTemplate(...)`, and store the
   returned `Hash128` in `setup.TemplateKey`.

3. **Move builders** from `Assets/Scripts/Skills/SkillSpawnTranslator.cs`
   (`BuildIntervalProjectileChild`/`BuildIntervalAoeChild` and the `Build*IntervalSpawner`
   helpers) into the driver walk (or a shared helper it calls). The translator's
   `SpawnProjectile`/`SpawnAoe` now read `setup.TemplateKey` (+ the cold timer config) instead
   of rebuilding the heavy template every cast.

## Acceptance criteria

- After `CompileAndRegister`, every interval setup has a non-default `TemplateKey` whose entry
  exists in the matching registry map.
- The translator no longer constructs `IntervalProjectileChild`/`IntervalAoeChild` per cast
  (verified by inspection / no per-cast allocation of those structs).
- Two slots compiling identical child behavior end up with the same `TemplateKey`.

## Dependencies

001, 002.

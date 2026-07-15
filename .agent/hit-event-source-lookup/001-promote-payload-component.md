# 001 — Promote CombatHitPayload to a shared component

## Goal
Make the fire-time damage/stack payload a single indexable component present on
every hit source (projectile + impact AOE + lingering AOE), so finalize can reach
it with one `ComponentLookup<CombatHitPayload>[Source]`.

## Changes

1. **`CombatHitPayload`** ([CombatHitPayload.cs](../../Assets/Scripts/System/Application/CombatHitPayload.cs)):
   add `: IComponentData`. Fields unchanged (DamageAmount, CritChance,
   CritMultiplier, DirectDamageEnabled, SourceNodeId, StackEffect). It remains
   usable as a plain nested field in configs/commands — the marker interface does
   not restrict that.

2. **`ProjectileHitComponent`** ([ProjectileEcsComponents.cs:29-34](../../Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs#L29-L34)):
   remove the nested `ProjectileHitPayload HitPayload`; add `OnHitSpawnRef OnHitSpawn`.
   Result: `{ int PierceRemaining; float RepeatHitCooldownSeconds; OnHitSpawnRef OnHitSpawn; }`.

3. **`AoeHitSpawnComponent`** ([AoeEcsComponents.cs:41-45](../../Assets/Scripts/System/Aoes/AoeEcsComponents.cs#L41-L45)):
   remove `CombatHitPayload HitPayload`. Result: `{ OnHitSpawnRef OnHitSpawn; }`.

4. **Archetypes**: add `CombatHitPayload` to the projectile, impact-AOE, and
   lingering-AOE archetypes wherever those entities are created/pooled (CombatRoot
   archetype/pool setup). Required so materialization's `SetComponentData` resolves.

5. **Materialization writers** — set the new component from the config's payload:
   - `ProjectileSpawnApplySystem` ([:336-341](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L336-L341)):
     write `ProjectileHitComponent { PierceRemaining, RepeatHitCooldownSeconds, OnHitSpawn = cfg…OnHitSpawn }`
     **and** the `CombatHitPayload` component from `cfg…HitPayload.HitPayload`.
     (`HitPayloadFor` at [:203] currently returns a `ProjectileHitPayload`; split it
     into the payload value + the OnHitSpawn ref.)
   - `AoeSpawnApplySystem` ([:578-582](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L578-L582)):
     write `AoeHitSpawnComponent { OnHitSpawn = cmd.OnHitSpawn }` **and** the
     `CombatHitPayload` component from `HitPayloadFor(cmd.HitPayload, cmd.Faction)`.

## Notes
- Keep the `ProjectileHitPayload` authoring DTO and all authoring call sites
  unchanged — only the entity write changes.
- Faction stamping into `StackEffect` currently happens inside `HitPayloadFor`;
  keep that logic, it now feeds the `CombatHitPayload` component.

## Acceptance
- Projectile + both AOE archetypes carry a `CombatHitPayload` component after spawn.
- `ProjectileHitComponent`/`AoeHitSpawnComponent` no longer contain the payload.
- Compiles only together with 002–004 (collision + finalize still reference old shape).

## Dependencies
Enables 003, 004. Scope: medium (touches component defs, archetype setup, 2 writers).

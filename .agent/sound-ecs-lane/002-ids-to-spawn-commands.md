# 002 — Carry sound ids to the spawn commands

## Why

`SkillSoundIds` already exists (`SoundEvent.cs:25-28`) and is already resolved
per root definition by the shipped `RegisterSounds` pass. Two things are missing
before an ECS job can emit:

1. Ids only exist for **root** definitions. The shipped pass is deliberately
   non-recursive (`implementation-log.md`: "non-recursive root-slot sound
   registration"), so an interval child or on-hit spawn carries `SpawnId == 0`.
2. Ids never reach ECS. They live on the managed `RuntimeSkillDefinition` and
   stop there.

VFX solves both with two stages this task copies.

## Change

### Recursive registration

Extend the shipped `RegisterSounds` in `SkillDriver` to walk the definition tree,
following `RegisterAoeTypesRecursive` (`SkillDriver.cs:1063-1117`) and covering
the same edges:

- `ChildSpawnSetup`, `AoeIntervalSpawnSetup`, `TargetedIntervalSpawnSetup`
- `ImpactAoeDefinition`, `ImpactProjectileDefinition`, `ImpactTargetedDefinition`
- `OnHitAoeSpawnDefinition`, `OnHitProjectileSpawnDefinition`,
  `OnHitTargetedSpawnDefinition`
- `StackingDetonation`

`AudioRoot.Register` dedupes by clip reference (`AudioRoot.cs:250`), so a shared
clip across many nodes still yields one id and re-running on every loadout edit
stays free. Depth guarding already exists in the sibling walks; match it rather
than inventing a second limit.

### Registry — ids per type

Add sound-id storage beside the VFX equivalents:

- `CombatRoot.SetAoeSoundIds` / `SetTargetedSoundIds`, beside `SetAoeVfxIds`
  (`CombatRoot.cs:902-907`).
- Backing storage on the type registries, beside `AoeTypeRegistry.SetVfxIds`
  (`:53`) and the targeted equivalent.

### Spawn commands — ids on the struct

Add `SoundIds` and `SpawnSoundRadius` to each spawn command, beside the existing
ids:

| Command | Place beside |
|---|---|
| `AoeSpawnCommand` | `public AoeVfxIds VfxIds;` (`AoeSpawnPipeline.cs:62`) |
| targeted spawn command | its `TargetedVfxIds` field |
| projectile spawn command | its render id — **projectiles have no `VfxIds` field**, because they author no VFX |

Stamp them wherever the VFX equivalents are stamped, so a command built by
`SkillIntervalTemplateBuilder` carries the child's own ids rather than its
parent's.

Templates are registered once through `RegisterSpawnTemplate` /
`RegisterTimedSpawnTemplate` and reused per spawn, so the ids ride along at no
per-spawn cost — the property that already makes `VfxIds` free.

## Acceptance Criteria

- An interval child, an on-hit spawn, and a stacking detonation each resolve
  their **own** `SpawnId`, not `0` and not their parent's.
- A shared clip across several nodes still yields one id.
- Ids remain stable across a loadout edit and recompile.
- A definition with no prefab, or a prefab with no clip, still resolves to `0`
  without throwing — the shipped null-guard behavior must not regress.
- The id and radius reach all three spawn command kinds.
- No `AudioClip` reference reaches ECS or any job — only `int` and `float`.
- `PlayGround.Sim.asmdef` unchanged.
- Recursion terminates on the deep and cyclic-looking shapes the sibling walks
  already guard.
- Nothing emits yet; this task is data plumbing only.

## Tests

EditMode. Add
`Assets/Tests/EditMode/SkillSoundRecursiveRegistrationEditModeTests.cs`:

- an interval child's own clip resolves to its own id — the regression guard for
  the recursive walk
- an on-hit spawn definition resolves its own id
- a clip shared between parent and child yields one id for both
- ids stable across recompile
- a null prefab anywhere in the tree resolves to `0` and does not throw

`SkillCastSoundEditModeTests` (shipped) must keep passing unchanged — this task
does not touch emission.

## Dependencies

[001](./001-sound-event-lane.md).

## Scope

Medium, wide but shallow — one recursive walk modelled on an existing one, two
registry methods, and an ids field on three command structs.

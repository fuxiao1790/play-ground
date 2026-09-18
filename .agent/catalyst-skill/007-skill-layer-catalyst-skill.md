# 007 — Skill layer: `CatalystSkill`

## Goal

Authoring, compilation, and cast for catalyst skills. A catalyst set plus an
`OnHitTrigger` link to any normal set is the whole authoring story for "fires
its own skill when hit".

## Changes

`Assets/Scripts/Skills/SkillDefinitionTags.cs`
- `Catalyst = 1 << 5`. **Not** added to `Any`: nothing may target a catalyst set
  through a trigger link, and leaving it out of `Any` makes the existing
  target-tag check produce the error for free.
- `SkillDefinitionTagUtility.Format` handles the new value.

`Assets/Scripts/Skills/Skill/CatalystSkill.cs` (new)
- `CatalystSkill : Skill`, `Tags => Catalyst | Duration`, holding
  `CatalystDefinition`. Menu path `PlayGround/Skills/Catalyst Skill`.

`Assets/Scripts/Skills/SkillDefinition.cs` (or beside it, matching the existing layout)
- `CatalystDefinition : SkillDefinition` with `DeepCopy()`:
  `prefab` (`CatalystPrefab`), `motionPattern`, `orbitRadius`,
  `angularSpeedDegrees`, `durationSeconds`, `maxCount`,
  `triggerAim`, `despawnOnOwnerLoss`, `manaCost`.
- No damage, crit, pierce, count, spread, or area fields. A catalyst deals no
  damage, and the absence is the enforcement.

`Assets/Scripts/Skills/Validator/CatalystPrefab.cs` (new)
- Sprite renderer plus one `Collider2D` body shape; `IsValidTemplate` requires a
  supported shape via `CombatTargetShapeUtility.IsSupportedShape` (circle,
  rectangle, capsule all pass) and a positive size. Optional spawn sound and
  spawn VFX slots, matching the other prefab types. No hurtbox semantics: the
  collider is the body, and it never produces a hit event.

`Assets/Scripts/Skills/Runtime/RuntimeCatalystDefinition.cs` (new)
- `RuntimeCatalystDefinition : RuntimeSkillDefinition` — resolved
  `DurationSeconds`, `MaxCount`, `OrbitRadius`, `AngularSpeedRadians`,
  `MotionPattern`, `TriggerAim`, `DespawnOnOwnerLoss`, baked shape fields,
  `ManaCost`, plus `OnHitSpawn` (`OnHitSpawnRef`) filled by the trigger link.
- `Damage` from the base class stays unused; do not fold it.

`Assets/Scripts/Skills/SkillSetCompiler.cs`
- Compile branch producing `RuntimeCatalystDefinition`:
  - `DurationSeconds = modifiers.Resolve(SkillStat.Duration, definition.durationSeconds)`
    — this is what makes `IncreasedDurationSupport` work with no new support.
  - `ManaCost` through `SkillStat.ManaCost`, `RecoveryTime = 1 / max(0.01, rate)`
    through `SkillStat.Rate` exactly as the other domains. High cooldowns are
    authored as a low `baseRate`; nothing new is needed for them.
  - `MaxCount` clamped to at least 1; `orbitRadius` and `angularSpeedDegrees`
    clamped to sane ranges with `TargetedParameterClamped`-style warnings.
- `AttachOnHitTarget` extended: a catalyst source with an `OnHitTrigger` stores
  the compiled effect's template key and kind in the catalyst's `OnHitSpawn`.
- `OnHitTrigger.SourceSkillTags` gains `Catalyst`. `IntervalSpawnTrigger` and
  `StackTrigger` from a catalyst source stay unsupported.
- Any link **targeting** a catalyst set is an `UnsupportedTriggerTarget` error
  and compiles to nothing.

`Assets/Scripts/Skills/SkillSpawnTranslator.cs`
- `RuntimeCatalystDefinition` branch → `combatRoot.SpawnRegisteredCatalyst(key, origin, faction, caster, manaCost, castToken)`.
  `origin` is the caster position; the anchor system places the body on the next
  update. `aimDir`/`aimWorldPos` are unused by the orbit pattern and are what a
  future placed pattern will consume.

`Assets/Scripts/Skills/SkillDriver.cs`
- Register catalyst render/type ids and the catalyst spawn template in the
  compile walk; add its key to `registeredTemplateKeys` so the existing
  register-new-then-release-old ordering covers it.
- Recursively register the catalyst's spawn sound like the other domains.

`Assets/Scripts/Skills/SkillLoadoutValidator.cs`
- Catalyst-specific warnings: missing prefab, unsupported collider shape
  (error — the body cannot spawn), non-positive duration or radius (error),
  `maxCount` clamped (warning), catalyst set with no outgoing `OnHitTrigger`
  (warning: the body is inert), any link targeting a catalyst set (error).

## Editor steps (user, not agent)

1. Create the catalyst prefab under `Assets/Prefabs/Skills/Catalyst/`: sprite
   renderer plus one circle, box, or capsule collider, and add `CatalystPrefab`.
2. `Assets > Create > PlayGround > Skills > Catalyst Skill`; assign the prefab,
   `durationSeconds`, `maxCount`, `orbitRadius`, `angularSpeedDegrees`,
   `baseRate` (high cooldown = low rate), `manaCost`.
3. Create a `SkillSet` for it; create a second set for the triggered effect.
4. In the loadout, wire `[Catalyst set] [OnHit] [effect set]`.
5. Assign the loadout to the player's `SkillDriver`.

## Acceptance criteria

- A catalyst set compiles to `RuntimeCatalystDefinition` with resolved duration,
  mana, and recovery time; `IncreasedDurationSupport` in the set increases the
  compiled duration.
- `[Catalyst] [OnHit] [Projectile]` fills `OnHitSpawn` with the projectile
  child's registered key and kind; the same holds for AOE and targeted effects.
- A link targeting a catalyst set reports `UnsupportedTriggerTarget` at error
  severity and produces no spawn setup.
- `IntervalSpawnTrigger` or `StackTrigger` from a catalyst source reports
  `UnsupportedTriggerSource`.
- A cast submits one request with the caster proxy and the compiled mana cost;
  rejection refunds the slot through the existing token path.
- Recompiling with a changed catalyst field registers the new key before
  releasing the old one.
- An unsupported collider shape blocks the cast and reports an error.

## Tests to run (EditMode, `CatalystCompileTests`)

- `CatalystSet_CompilesRuntimeDefinitionWithResolvedStats`
- `DurationSupport_IncreasesCompiledDuration`
- `OnHitTrigger_FillsCatalystTriggerRef_ForEachEffectKind`
- `LinkTargetingCatalystSet_IsError`
- `IntervalOrStackFromCatalyst_IsUnsupportedSource`
- `UnsupportedColliderShape_BlocksSpawnWithError`
- `CatalystSetWithoutOnHitLink_WarnsInert`

## Dependencies

001 for the `CombatRoot` API and cast gate; 002 for the component shapes the
command must fill. Can be written in parallel with 005/006 — the compile path
does not depend on the hit path.

## Scope

Large by file count, low by risk: every edit follows the shape of an existing
domain in the same files.

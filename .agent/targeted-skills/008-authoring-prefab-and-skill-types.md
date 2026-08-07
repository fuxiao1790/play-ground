# 008 — Authoring: prefab type, skill types, definitions

**Depends on:** nothing (pure authoring types). **Scope:** medium. **Risk:** low.

## Why

The player-facing surface, mirroring `BasicAoePrefab` / `AoeSkill` / `LingeringAoeSkill`
(requirements decision 4). Two skill types, one prefab type, no hurtbox.

**Not in this task: `TargetedTypeDefinition`.** That is the Sim-assembly registration type beside
`CombatRoot`, mirroring `AoeTypeDefinition`, and it lands in **task 007**. The two describe similar
content but sit on opposite sides of the assembly boundary: `TargetedPrefab` here is authoring that
`PlayGround.GameLogic` owns, `TargetedTypeDefinition` is what `PlayGround.Sim` registers. Task 009
converts one into the other, exactly as `SkillDriver` converts a `RuntimeAoeDefinition` into an
`AoeTypeDefinition` before calling `CombatRoot.RegisterType`.

## New files

`Assets/Scripts/Skills/Validator/TargetedPrefab.cs`

Follows `BasicAoePrefab`'s shape **minus collision**:

- `[SerializeField] SpriteRenderer spriteRenderer` — **optional**, same rule `BasicAoePrefab` uses:
  if present with a sprite it must be on a child named `Visual`, and its material must be non-null,
  textured, GPU-instanced, and on a supported shader. Absent or sprite-less → VFX-only.
- VFX asset slots with a `VfxDataShape` each, following `BasicAoePrefab`'s spawn/hit/expire/arming
  set, plus a **link** slot that must be `LineSegment`.
- `[SerializeField] float vfxEffectSize` — the radius the circular spawn / hit / expire / arming
  effects are dispatched at. An AOE derives this from its gameplay area; **a chain has no area**,
  so it must be authored. Visual-only: it does not fold through the `AreaSize` stat and does not
  affect targeting.
- `[SerializeField] float linkWidth` — the `LineSegment` width.
- **No `hurtbox` field.** No `Radius`, `HalfExtents`, `RotationRadians`, or `ShapeType` accessors —
  a targeted skill resolves by query, so there is no shape to extract.
- `IsValidTemplate(out string reason)` **fails** when a child named `Hurtbox` exists. A physics
  shape on a non-physics skill means the prefab was copied from a projectile or AOE template and
  the assumption came with it. This is the guard, not a comment.
- `Awake()` throws `MissingReferenceException` on invalid setup, matching both existing prefabs.

`Assets/Scripts/Skills/Skill/TargetedSkillBase.cs`, `TargetedSkill.cs`,
`LingeringTargetedSkill.cs`

- `TargetedSkillBase : Skill` with `Tags => SkillDefinitionTags.Targeted`, mirroring `AoeSkillBase`.
- `TargetedSkill` holds `TargetedDefinition`; menu
  `PlayGround/Skills/Targeted Skill`.
- `LingeringTargetedSkill` holds `LingeringTargetedDefinition`; menu
  `PlayGround/Skills/Lingering Targeted Skill`.

`Assets/Scripts/Skills/SkillDefinition.cs` additions (or a sibling file)

```text
TargetedDefinition                     (single hit)
 ├─ prefab:    TargetedPrefab
 └─ behavior:  damage, manaCost, directDamageEnabled,
               count,
               acquireRadius, maxTargets, chainRadius,
               chainDamageFalloff, chainDelaySeconds, armSeconds

LingeringTargetedDefinition            (interval tick)
 └─ behavior:  ...all of the above, plus lifetimeSeconds, tickIntervalSeconds
```

- `DeepCopy()` on both, matching the existing definitions.
- Following the AOE precedent, `TargetedDefinition` does **not** expose `lifetimeSeconds` or
  `tickIntervalSeconds`; the runtime receives `0` for both.
- `count` has **no** paired spread or scatter field. Projectile `spreadDegrees` and AOE
  `scatterRadius` exist because those domains place a shape in the world; a chain places nothing
  (requirements decision 14).

## User steps (editor work — not performed by the agent, C14)

Written up in task 015 and listed here so the dependency is visible:

1. Create `Assets/Prefabs/Combat/TargetedChain.prefab` with a `TargetedPrefab` component. Add a
   `Visual` child with a `SpriteRenderer` **only** if a debug sprite is wanted; leave it empty
   otherwise. **Do not add a `Hurtbox` child** — validation rejects it.
2. If a debug sprite is used, add it to the combat sprite atlas and repack, and confirm its
   material has GPU instancing enabled.
3. Create the `LineSegment` VFX graph and assign it to the prefab's link slot with shape
   `LineSegment`.
4. Create the skill SOs via the new menu entries and fill in the behaviour fields.

## Acceptance criteria

- EditMode: a `TargetedPrefab` with no sprite renderer validates successfully.
- EditMode: a `TargetedPrefab` with a `Visual` sprite renderer and a valid material validates.
- EditMode: a sprite renderer on a child **not** named `Visual` fails with a clear reason.
- EditMode: a material without GPU instancing fails with a clear reason.
- EditMode: **a prefab containing a `Hurtbox` child fails validation**, with a reason naming the
  copied-template mistake.
- EditMode: `TargetedSkill.Tags` and `LingeringTargetedSkill.Tags` are `SkillDefinitionTags.Targeted`.
- EditMode: `DeepCopy()` on both definitions produces an independent instance — mutating the copy
  does not touch the SO template.
- Both menu entries appear under `Assets > Create > PlayGround > Skills`.

## Notes

Test prefabs belong under `Assets/Tests/`, never in runtime folders. The hurtbox-rejection test
needs a fixture prefab with a `Hurtbox` child — that fixture is itself editor work, so either build
it in code inside the test or list it as a user step.

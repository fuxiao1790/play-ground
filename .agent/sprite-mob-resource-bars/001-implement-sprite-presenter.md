# 001 — Implement Child Sprite Presenter

## Change

Add `Assets/Scripts/Mob/MobResourceBarSprite.cs` in `PlayGround.GameLogic`.
Component lives on a `ResourceBar` child beneath each authored `MobRoot`.

Serialized references/configuration:

- background `SpriteRenderer`;
- fill `SpriteRenderer`;
- fill Transform (or derive once from assigned fill renderer);
- visible fill steps, default/minimum 36.

Runtime contract:

- cache full-fill local scale in `Awake` and validate assigned renderers;
- expose `SetHealth(float current, float max)`;
- clamp ratio to `[0,1]`, quantize to an integer step, and write local X scale
  only when the step changes;
- expose alive/visibility inputs and combine them before changing both
  renderer-enabled states;
- bind one `GameSettings` instance, applying `DisplayMobHealthBars` and
  subscribing in `OnEnable`/unsubscribing in `OnDisable`;
- preserve assigned Y/Z scale and left-anchored fill behavior;
- no `Update`, `LateUpdate`, camera access, scene search, material clone, or
  managed allocation in refresh methods.

Component assumes one user-authored shared rectangular white sprite. Creating
or importing that asset and configuring its pivot/import settings belongs to
task 003 and must not be performed by the agent.

## Acceptance Criteria

- Component validates missing background/fill assignments with exact,
  actionable setup errors.
- Repeated `SetHealth` calls within the same quantized step perform no Transform
  write.
- Step 0 has zero X fill scale; step 36 restores captured full X scale.
- Setting visibility and alive visibility compose correctly; enabling settings
  cannot make a dead mob bar visible.
- Component source contains no frame callback and no health/gameplay authority.
- Component uses assigned shared sprite/material through normal serialized
  `SpriteRenderer` references and never creates a material instance.

## Dependencies

None.

## Scope / Complexity

Medium: one runtime component. No serialized asset/prefab changes.

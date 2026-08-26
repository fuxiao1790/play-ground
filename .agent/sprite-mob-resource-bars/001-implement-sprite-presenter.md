# 001 — Implement Child Sprite Presenter

## Change

Add `Assets/Scripts/Mob/MobResourceBarSprite.cs` in `PlayGround.GameLogic`.
Component lives on a `ResourceBar` child beneath each authored `MobRoot`.

Serialized references/configuration:

- background `SpriteRenderer`;
- fill `SpriteRenderer`.

Runtime contract:

- cache full-fill local scale from the fill renderer's own transform and
  validate assigned renderers through an idempotent `EnsureInitialized()`
  guard called from `Awake` *and* from the top of every public method, not
  from `Awake` alone. Unity does not guarantee a parent GameObject's `Awake`
  runs after its children's `Awake`, so `MobRoot.Awake` -> `InitializeForSpawn`
  can call into this component before its own `Awake` has run; the guard
  makes every entry point correct regardless of that ordering (same pattern
  already used by `GameSettings.EnsureInitialized`). No separate
  fill-transform override field (removed as unjustified — no prefab needs a
  wrapper transform between the fill renderer and its own scale);
- expose `SetHealth(float current, float max)`;
- clamp ratio to `[0,1]` and write local X scale directly from the continuous
  ratio on every call; no step-quantization layer (removed by user
  direction — premature optimization; `Resource.Changed` already gates calls
  to only actual health changes, so re-add quantization later only if
  profiling in task 006 shows per-tick writes are a real cost);
- expose alive/visibility inputs and combine them before changing both
  renderer-enabled states;
- bind one `GameSettings` instance, applying `DisplayMobHealthBars` and
  subscribing in `OnEnable`/unsubscribing in `OnDisable`;
- preserve assigned Y/Z scale and left-anchored fill behavior;
- no `Update`, `LateUpdate`, camera access, scene search, material clone, or
  managed allocation in refresh methods.

Component assumes two user-authored shared rectangular sprites (one for
background, one for fill; see 003). Creating or importing those assets and
configuring their pivot/import settings belongs to task 003 and must not be
performed by the agent.

## Acceptance Criteria

- Component validates missing background/fill assignments with exact,
  actionable setup errors, regardless of whether the component's own `Awake`
  has run yet by the time a public method is first called.
- `SetHealth` writes the fill's local X scale proportional to `current/max`,
  clamped to `[0,1]`, on every call.
- Ratio 0 yields zero X fill scale; ratio 1 restores captured full X scale.
- Setting visibility and alive visibility compose correctly; enabling settings
  cannot make a dead mob bar visible.
- Component source contains no frame callback and no health/gameplay authority.
- Component uses assigned shared sprite/material through normal serialized
  `SpriteRenderer` references and never creates a material instance.

## Dependencies

None.

## Scope / Complexity

Medium: one runtime component. No serialized asset/prefab changes.

# 003 — User Inspector Authoring

## Ownership Boundary

User performs this task in Unity Editor. Agent must pause, provide/check these
instructions, and wait for user confirmation. Agent must not hand-edit
`.prefab`, `.unity`, `.asset`, or generated `.meta` files and must not use an
editor script/builder to automate the work.

## User Changes

Create/import one shared rectangular white sprite under `Assets/Sprite/UI/`:

- Texture Type: Sprite (2D and UI);
- Sprite Mode: Single;
- Mesh Type: Full Rect;
- Pivot: Custom, left-center `(0, 0.5)`;
- no per-prefab material instance;
- assign one shared sprite-compatible material to every bar renderer.

For Bat, Slime, and Skeleton prefabs:

- add root child `ResourceBar` with `MobResourceBarSprite`;
- add background/fill children with `SpriteRenderer` components;
- assign shared sprite/material;
- assign dark background and red fill colors matching old USS intent;
- use approximately 36:5 bar aspect;
- use same sorting layer as mob visual with stable orders above current order 9;
- assign background/fill references on `MobResourceBarSprite`;
- assign child presenter into `MobRoot.resourceBar`;
- visually tune `ResourceBar.localPosition` and local size separately per prefab;
  old offsets Bat `0.5`, Slime `0.8`, Skeleton `0.6` are starting references,
  not runtime fields.

In `BenchmarkLarge`:

- assign `GameRoot.gameSettings` to existing `GameSettings` on `GameRoot`;
- remove now-empty `LabelUI` root GameObject in Hierarchy;
- delete `Assets/UI/WorldLabelsPanel.asset` through Project window after
  confirming nothing references it.

Save prefab assets and scene through Unity Editor, then tell agent authoring is
complete. Agent performs read-only verification before continuing.

## Acceptance Criteria

- All three mob prefabs show independently positioned world-space sprite bars.
- Prefabs have no missing component, sprite, material, or serialized reference.
- Scene contains no `LabelUI` or world-label `UIDocument`.
- `GameRoot.gameSettings` is assigned.
- Shared sprite/material identity is consistent across every bar renderer.

## Dependencies

- 001 — presenter exists and compiles.
- 002 — serialized fields and binding APIs exist and compile.

## Scope / Complexity

User-owned medium Inspector task. Agent makes zero serialized-file changes.


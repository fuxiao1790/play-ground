# 003 — User Inspector Authoring

## Ownership Boundary

User performs this task in Unity Editor. Agent must pause, provide/check these
instructions, and wait for user confirmation. Agent must not hand-edit
`.prefab`, `.unity`, `.asset`, or generated `.meta` files and must not use an
editor script/builder to automate the work.

## User Changes

Create/import two shared rectangular sprites under `Assets/Sprite/UI/` — custom
art per role, not one tinted placeholder:

- Background sprite: Texture Type Sprite (2D and UI); Sprite Mode Single; Mesh
  Type Full Rect; pivot is not load-bearing since background never scales at
  runtime.
- Fill sprite: Texture Type Sprite (2D and UI); Sprite Mode Single; Mesh Type
  Full Rect; Pivot: Custom, left-center `(0, 0.5)` — required, this is what
  keeps the fill anchored to the left as it drains.
- No per-prefab material instance. Assign one shared background material to
  every background renderer across all three prefabs, and one shared fill
  material to every fill renderer across all three prefabs (two shared
  sprite/material pairs total, reused by role).

For Bat, Slime, and Skeleton prefabs:

- add root child `ResourceBar` with `MobResourceBarSprite`;
- add background/fill children with `SpriteRenderer` components;
- assign the shared background sprite/material to each background renderer and
  the shared fill sprite/material to each fill renderer;
- tint via `SpriteRenderer.color` only if your art needs it — dark
  background/red fill from the old USS is a starting reference, not a
  requirement, now that art is custom;
- use approximately 36:5 bar aspect;
- use same sorting layer as mob visual with stable orders above current order 9;
- assign background/fill references on `MobResourceBarSprite`;
- assign child presenter into `MobRoot.resourceBar`;
- visually tune `ResourceBar.localPosition` and local size separately per prefab;
  old offsets Bat `0.5`, Slime `0.8`, Skeleton `0.6` are starting references,
  not runtime fields.

In `BenchmarkLarge`:

- assign `GameRoot.gameSettings` to the `GameSettings` component already on the
  `GameRoot` GameObject;
- ~~remove now-empty `LabelUI` root GameObject~~ — confirmed absent by agent
  read-only check; scene has no `LabelUI` object and no `UIDocument`/
  `MobResourceBarUi` wiring at all today, so nothing to remove here;
- optionally delete `Assets/UI/WorldLabelsPanel.asset` through Project window —
  agent confirmed zero references anywhere in `Assets/`, safe any time (can
  also be left for task 004).

Save prefab assets and scene through Unity Editor, then tell agent authoring is
complete. Agent performs read-only verification before continuing.

## Acceptance Criteria

- All three mob prefabs show independently positioned world-space sprite bars.
- Prefabs have no missing component, sprite, material, or serialized reference.
- Scene contains no `LabelUI` or world-label `UIDocument`.
- `GameRoot.gameSettings` is assigned.
- Background sprite/material identity is consistent across every background
  renderer; fill sprite/material identity is consistent across every fill
  renderer.

## Dependencies

- 001 — presenter exists and compiles.
- 002 — serialized fields and binding APIs exist and compile.

## Scope / Complexity

User-owned medium Inspector task. Agent makes zero serialized-file changes.


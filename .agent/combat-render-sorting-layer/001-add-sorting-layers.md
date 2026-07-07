# 001 - Add Sorting Layers

## Scope

Project-settings change only, no code.

## Change

In the Editor (Project Settings > Tags and Layers > Sorting Layers), add two
new Sorting Layers. Unity's Sorting Layer list order is bottom-to-top in
render order, with list position 0 (`Default`, currently the only entry,
`uniqueID: 0`) rendering first. Insert the two new layers **before** `Default`
in the list so they render first (bottom):

1. `CombatVfx`
2. `CombatSprites`
3. `Default` (unchanged, stays where it is — now third instead of first)

Resulting bottom-to-top order: `CombatVfx` → `CombatSprites` → `Default`
(player + mob, still Y-sorted against each other within `Default`).

Do this via the Editor UI, not by hand-editing
`ProjectSettings/TagManager.asset` — Unity assigns `uniqueID` values
internally and a manual YAML edit risks an ID collision.

## Acceptance Criteria

- `ProjectSettings/TagManager.asset` lists `CombatVfx`, `CombatSprites`,
  `Default` in that order under `m_SortingLayers`.
- No existing `SpriteRenderer`/`MeshRenderer` in the project was using a
  Sorting Layer other than `Default` (quick project search before merging;
  see index.md Open Questions) — if one was found, resolve before proceeding.

## Dependencies

None. Can be done independently of all other subtasks.

## Complexity

Trivial (Editor Project Settings change), but blocks visual verification of
every other subtask, so do it first.

---
name: ui-folder-reorg
description: Consolidate Assets/Scripts/SkillUi (C#) and Assets/Scripts/Ui (UXML/USS) into Assets/Scripts/Ui, organized by UI component instead of by file type
---

# UI Folder Reorg

## Summary

Today UI-related files are split by file type across two folders:

- `Assets/Scripts/Ui/` — UXML + USS only (`SkillLoadout/`, `ResourceBars/`)
- `Assets/Scripts/SkillUi/` — all C# controllers/catalogs + the `PlayGround.SkillUi.asmdef`

This plan merges everything into `Assets/Scripts/Ui/`, grouped by UI component
(controller + the markup/style it owns), and renames the assembly to
`PlayGround.Ui` to match. `PlayerSaveController.cs`, which lives in `SkillUi/`
today but is not UI code, moves to `Assets/Scripts/Persistence/` alongside
`PlayerSaveStore.cs`/`PlayerSaveData.cs` instead.

## Grounding

- [Docs/ui.md](../../Docs/ui.md) — UI architecture: `PlayGround.SkillUi` assembly
  owns feature-specific UI controllers, one-way dependency
  `PlayGround.SkillUi -> PlayGround.GameLogic -> PlayGround.Sim`; UXML/USS/C#
  split rules.
- [Docs/folder-structure.md](../../Docs/folder-structure.md) — current assembly
  map, including `Assets/Scripts/SkillUi/PlayGround.SkillUi.asmdef`.
- Scene inspection (`Assets/Scenes/BenchmarkLarge.unity`, GameObject `GameUI`,
  `fileID: 1983018953`) — confirms `UIDocument` (source asset
  `SkillLoadoutUi.uxml`, guid `fa59a93c...`), `SkillLoadoutUi`,
  `PlayerSaveController`, `GameplayInputSurface`, `ResourceBarUi`, and
  `PerformanceUi` are **all components on one GameObject sharing one
  `UIDocument`/root UXML**. `SkillLoadoutUi.uxml` is a shared HUD root, not a
  private template of the skill bar.
- `SkillLoadoutUi.uxml` pulls in `../ResourceBars/ResourceBarsUi.uss` for the
  `.resource-orb*` classes used by its own inline health/mana markup.
  `ResourceBarsUi.uxml` (the template with a `resource-bars`/`ProgressBar`
  root) has no `UIDocument` or `VisualTreeAsset` reference anywhere in the
  project — dead file, confirmed via project-wide grep.
- `PlayerSaveController.cs` has no `UIElements`/`UIDocument` usage; it is
  autosave/persistence orchestration. `Assets/Scripts/Persistence/` already
  exists (`PlayerSaveStore.cs`, `PlayerSaveData.cs`, both `namespace
  PlayGround.Persistence`) under the single root `PlayGround.GameLogic.asmdef`
  covering `Assets/Scripts/` and its non-asmdef subfolders.
- No other `.asmdef` lists `PlayGround.SkillUi` in its `references` (grepped
  project-wide); the rename to `PlayGround.Ui` is isolated to this asmdef plus
  docs.
- Scene/prefab/asset `m_EditorClassIdentifier` fields (e.g.
  `PlayGround.SkillUi::PlayGround.Skills.SkillLoadoutUi`) are editor-display
  cosmetics — resolution is by `m_Script: {fileID, guid}`. Moving files,
  renaming the assembly, or changing a namespace does **not** break
  scene/prefab wiring as long as each `.meta` (and its `guid`) moves with its
  file. Confirmed no other `.cs` file references `PlayerSaveController`,
  `GameplayInputSurface`, `ResourceBarUi`, `SkillLoadoutUi`, or the catalog
  types by name outside `SkillUi/` itself.

## User decisions (asked, not assumed)

1. **Grouping shape:** shared root + nested component. A `Hud/` folder holds
   the shared root UXML/USS plus the controllers bound directly to it
   (`GameplayInputSurface.cs`, `ResourceBarUi.cs`); a nested
   `Hud/SkillLoadout/` folder holds only the skill-bar-private templates,
   catalogs, and `SkillLoadoutUi.cs`.
2. **`PlayerSaveController.cs`:** relocate out of `Ui/` entirely, into
   `Assets/Scripts/Persistence/` (not UI-related).
3. **Assembly rename:** `PlayGround.SkillUi` → `PlayGround.Ui` (folder now
   matches assembly name).
4. **Dead `ResourceBarsUi.uxml`:** delete it. Its `.uss` sibling is real
   (used) and moves/renames to `Hud/ResourceBarUi.uss` for naming consistency
   with `ResourceBarUi.cs` (existing `ResourceBarUi.cs` vs `ResourceBarsUi.uxml/uss`
   plural mismatch is incidental, not deliberate).

## Constraints & invariants

- **Scene/prefab safety:** every moved `.cs`/`.uxml`/`.uss` must keep its
  paired `.meta` file (same `guid`) so `Assets/Scenes/BenchmarkLarge.unity`'s
  component references keep resolving. Source: Unity meta/GUID resolution
  model, confirmed above.
- **Assembly boundary:** `PlayGround.Ui` (post-rename) must keep depending
  only on `PlayGround.GameLogic` → `PlayGround.Sim`, never the reverse.
  Source: [Docs/ui.md](../../Docs/ui.md) "Ownership" section.
- **UXML relative `Style src` paths** are resolved relative to the `.uxml`
  file's own folder; every file move that changes a UXML's folder, or the
  folder of a `.uss` it references, requires updating that `<Style src="...">`
  path in the same change. Source: current `SkillLoadoutUi.uxml` content
  (`<Style src="SkillLoadoutUi.uss" />`, `<Style src="../ResourceBars/ResourceBarsUi.uss" />`).
- **No editor-only steps required:** this is a pure file relocation + text
  edit (paths, JSON, namespace, docs); nothing here requires Unity Inspector
  wiring or regenerating asset content, so it's safe to execute directly
  (see [[editor-steps-are-user-steps]] — that rule is about *hand-authoring*
  YAML/asset content to simulate Inspector work, not about moving existing
  file+meta pairs intact).

## Mechanisms reused vs. introduced

- Reused: existing `Assets/Scripts/Persistence/` folder and its
  `PlayGround.Persistence` namespace for `PlayerSaveController.cs` — no new
  folder or namespace invented for it.
- Reused: existing one-root-asmdef-per-`Assets/Scripts/` pattern; `Ui/` keeps
  being its own asmdef the same way `Sim/`, `Debugging/` are.
  Introduced: `Hud/` as a new grouping folder — justified because it's the
  first time the "one shared root document, several controllers" shape is
  made explicit in the folder layout; without it, the shared UXML/USS would
  have to live arbitrarily inside one controller's folder while others
  reach into it by relative path (the status quo problem this task is fixing).

## Design validation

- Scene safety invariant: satisfied by moving every `file + file.meta` pair
  together, never regenerating a `.meta`. Task 001–003 each end with a GUID
  diff check.
- Assembly boundary invariant: satisfied — no dependency edges change, only
  the `name`/`rootNamespace` fields of the one asmdef being renamed.
- UXML relative path invariant: satisfied — task 002 enumerates the exact
  `Style src` edits required by the new folder depths.

## Minimal/additive vs. refactor comparison

- **Minimal/additive:** move C# files into the existing `Ui/SkillLoadout/`
  and `Ui/ResourceBars/` folders as-is (file-type merge only, no regrouping).
  - resulting data flow: unchanged; two folders keep pretending
    `ResourceBars/` is a self-contained component when its only live
    artifact is a stylesheet consumed by `SkillLoadout/`.
  - new concepts/types introduced: none.
  - copies/translations added: none.
  - long-term cost: keeps the misleading "ResourceBars is its own component"
    folder alive, and still forces the next person to guess why a
    `SkillLoadout`-named UXML holds the entire HUD (resource orbs,
    performance panel, input surface).
- **Refactor (chosen):** introduce `Hud/` as the shared-root folder, nest
  `SkillLoadout/` under it, delete the dead template, rename the mismatched
  `ResourceBarsUi.uss` file, rename the assembly to match its folder.
  - resulting data flow: unchanged at runtime (same GameObject, same
    `UIDocument`, same queries) — only source layout changes.
  - existing concepts/types changed or removed: `ResourceBarsUi.uxml`
    deleted (dead); `PlayGround.SkillUi` asmdef renamed; `ResourceBarsUi.uss`
    renamed to `ResourceBarUi.uss`.
  - copies/translations removed or avoided: removes the cross-folder
    `../ResourceBars/...` relative reference — the stylesheet now lives next
    to the document that actually uses it.
  - long-term benefit: folder layout matches the real architecture (one HUD
    document, several controllers), so the next feature added to the HUD has
    an obvious place to go instead of another ad hoc top-level folder.
  - Decision: **refactor** (this is what the user asked for and confirmed
    per-question above).

## Task list

1. [001-move-player-save-controller.md](001-move-player-save-controller.md) —
   relocate `PlayerSaveController.cs` to `Assets/Scripts/Persistence/`,
   namespace `PlayGround.Skills` → `PlayGround.Persistence`.
2. [002-build-hud-root.md](002-build-hud-root.md) — create
   `Assets/Scripts/Ui/Hud/`; move `GameplayInputSurface.cs`,
   `ResourceBarUi.cs`, `SkillLoadoutUi.uxml`, `SkillLoadoutUi.uss` into it;
   delete dead `ResourceBarsUi.uxml`; move+rename `ResourceBarsUi.uss` →
   `Hud/ResourceBarUi.uss`; fix `Style src` paths; remove the emptied
   `Ui/ResourceBars/` folder.
3. [003-nest-skillloadout.md](003-nest-skillloadout.md) — move
   `SkillLoadoutUi.cs`, the three catalog scripts, and the five template
   `.uxml` files into `Assets/Scripts/Ui/Hud/SkillLoadout/`.
4. [004-rename-asmdef.md](004-rename-asmdef.md) — move
   `PlayGround.SkillUi.asmdef` to `Assets/Scripts/Ui/PlayGround.Ui.asmdef`,
   rename its `name`/`rootNamespace` fields to `PlayGround.Ui`; delete the
   emptied `Assets/Scripts/SkillUi/` folder.
5. [005-update-docs.md](005-update-docs.md) — update `Docs/ui.md`,
   `Docs/folder-structure.md`, `Docs/architecture/layer-rules.md` paths and
   assembly name.
6. [006-verify.md](006-verify.md) — grep for stale paths/assembly name,
   confirm every moved file's `.meta` guid is unchanged, print final tree.

## Open questions / follow-ups (not part of this task)

- The C# files being moved into `Ui/Hud/` and `Ui/Hud/SkillLoadout/` currently
  use inconsistent namespaces (`PlayGround.Skills` for most, `PlayGround.SkillUi`
  for `ResourceBarUi`/`PerformanceUi`), and neither matches the assembly's
  `rootNamespace`. This task does **not** touch those namespaces — only
  `PlayerSaveController.cs` gets a namespace change, because it's moving next
  to files that already establish `PlayGround.Persistence` as the folder's
  convention. Flagging in case a follow-up namespace cleanup is wanted later.
- `PerformanceUi.cs` (already mid-move to `Assets/Scripts/Debugging/` in your
  working tree, uncommitted) queries elements that live in
  `Ui/Hud/SkillLoadoutUi.uxml` after this task. That cross-folder dependency
  already exists today (`Assets/Scripts/SkillUi/PerformanceUi.cs` depending on
  `Assets/Scripts/Ui/SkillLoadout/SkillLoadoutUi.uxml`); this task only moves
  where the UXML lives, it doesn't change or fix that coupling.
- I did not touch the unrelated pending changes already in your working tree
  (`Assets/Scripts/Debugging/PerformanceUi.cs`/`PerformanceText.cs`, the
  deleted `.agent/targeted-*` files) — left exactly as found.

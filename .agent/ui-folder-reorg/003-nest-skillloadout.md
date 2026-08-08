---
name: 003-nest-skillloadout
description: Move the skill-bar-private controller, catalogs, and templates into Ui/Hud/SkillLoadout/
---

# Nest SkillLoadout Under Hud/

## Scope

Everything here is private to the skill bar sub-feature (not shared with
resource orbs, performance panel, or input surface): the controller, the
three catalog ScriptableObjects it and `SkillLoadoutUi` use, and the five
UXML templates `SkillLoadoutUi.cs` clones at runtime.

## Changes

1. Move (reusing the existing `Ui/SkillLoadout/` folder — rename/relocate it
   to `Ui/Hud/SkillLoadout/`, preserving its folder `.meta` guid
   `c5b071e77936ab944840bb6c16ac55e3`):
   - `Assets/Scripts/Ui/SkillLoadout/SkillNodeColumn.uxml` (+ `.meta`, guid
     `707e9a4867132d244867fa216fcc4cb4`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillSupportButton.uxml` (+ `.meta`,
     guid `8d79d196e75c9bc48bb17585cd55a266`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillTriggerButton.uxml` (+ `.meta`,
     guid `66fa4f154002c1740ad5389adbb51725`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillPicker.uxml` (+ `.meta`, guid
     `e0ab1cebbe2e2bd46bc9ab86e0aeaf59`)
   - `Assets/Scripts/Ui/SkillLoadout/SkillPickerChoice.uxml` (+ `.meta`,
     guid `f6b6353aee6018b49bb4e8e4839f78cd`)
     (none of these five have `Style src` or template `src` references to
     fix — confirmed each is a standalone `<ui:UXML>` with no `<Style>` tag)
2. Move from `Assets/Scripts/SkillUi/` into `Assets/Scripts/Ui/Hud/SkillLoadout/`:
   - `SkillLoadoutUi.cs` (+ `.meta`, guid `14f58db2ed082d648b03b9baaf79d898`)
   - `SkillUiCatalog.cs` (+ `.meta`, guid `37b563012b797124fbcf658643842a51`)
   - `SkillUiSupportCatalog.cs` (+ `.meta`, guid
     `18b65b0aa3494714b43cf863ea423186`)
   - `SkillUiTriggerCatalog.cs` (+ `.meta`, guid
     `525d8d68b8e94c93a5c02c8a3978d20b`)
3. Do not change any namespace in this task — `SkillLoadoutUi.cs`,
   `SkillUiCatalog.cs`, `SkillUiSupportCatalog.cs`, `SkillUiTriggerCatalog.cs`
   keep `namespace PlayGround.Skills` (see index.md "Open questions" — this
   is a known pre-existing inconsistency, out of scope here).
4. `SkillLoadoutUi.cs` has `[SerializeField] private VisualTreeAsset
   nodeColumnTemplate` etc. — these are Inspector-assigned object references
   (resolved by guid), not path strings, so moving the `.uxml` files does not
   require touching `SkillLoadoutUi.cs`.

## Acceptance criteria

- `Assets/Scripts/Ui/Hud/SkillLoadout/` contains all 9 files listed above,
  each with its original guid preserved.
- `Assets/Scripts/Ui/SkillLoadout/` no longer exists as a separate top-level
  folder under `Ui/` (it's now nested under `Hud/`).
- No `.cs` file content changed in this task beyond location.

## Dependencies

Must run after task 002 (needs `Ui/Hud/` to exist as the parent).

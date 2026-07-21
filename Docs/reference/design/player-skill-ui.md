# Player Skill UI

## Purpose

Define the v1 player-facing skill-bar and picker behavior. The UI projects
game-logic state and sends edit commands; it owns no loadout, cooldown, or
validation result.

## V1 Layout Policy

The bottom-center bar defaults to three visible skill positions and two links
between adjacent positions. `SkillLoadoutUi.initialNodeCount` serializes this
initial visible/runtime node count. An equipped skill set displays up to the
maximum support count authored on its `Skill`; an empty skill position has no
support row. The UI does not own or override this gameplay limit. `SkillLoadout`
and `SkillSet` collections remain unbounded data
structures. V1 does not choose a player root-skill limit.

```text
[ supports: per set ]  [ supports: per set ]  [ supports: per set ]
  [ skill 0 ] -- link 0 -- [ skill 1 ] -- link 1 -- [ skill 2 ]
```

An empty skill position displays `+`. A node with an incoming trigger is gray
and labelled triggered-only. It remains clickable for editing. A direct-cast
root displays its cooldown overlay. Triggered-only nodes have no direct-cast
cooldown overlay.

## Picker

Selecting any visible skill, support, or trigger opens one top-center modal
picker for that target. It has a title, choices from the relevant catalog group,
Clear/None where valid, disabled choices with a short reason, and a pending
state after submit. It supports pointer selection, keyboard focus/navigation,
scrolling, Escape, and backdrop cancel. Its commands use the latest displayed
loadout revision.

## Input And Time

The picker never changes `Time.timeScale`. CPU simulation, ECS combat, and GPU
VFX continue. While the modal is open, `PlayerRoot` blocks gameplay actions but
keeps UI input active. When the pointer is over the skill bar or picker,
`PlayerRoot` suppresses Attack for that frame so a UI click cannot cast. This is
an input-routing gate, not a second input owner. Keyboard/gamepad structure is
kept focus-based, but full gamepad and touch acceptance are out of scope.

## Edit Feedback

The picker stays pending until `EditResolved`. On success it closes or refreshes
from the new driver revision. On rejection it keeps current equipment, clears
pending state, and presents the reason. UI must not optimistically mutate a
local loadout projection.

## Visual Scope

Existing skill art may be used. Missing support/trigger art uses consistent
colored initials. Final icon art, animation polish, touch support, and full
gamepad acceptance are later work.

## Sample Scope

The playable sample uses a dedicated valid three-node loadout and catalog. It
must not bind this fixed v1 view to `BenchmarkHybridLoadout` or hide extra nodes
from that benchmark asset.

## Related Documents

- [Skill Loadout Editing](../../contracts/skill-loadout-editing.md)
- [Skill Loadout Edit Flow](../../flows/skill-loadout-edit.md)

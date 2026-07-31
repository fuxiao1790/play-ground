---
name: verify-energy-gain-stat-sheet-assets
description: User/editor step - confirm existing UnitStatSheet assets keep compiling to identical EnergyPerSecond and optionally author non-neutral values
---

# 005 — Verify Energy Gain Stat Sheet Assets (User/Editor Step)

## Scope

This is a Unity-editor step for the user, not code — per
[[editor-steps-are-user-steps]], ScriptableObject asset authoring/inspection
is never hand-edited as YAML by the agent. Depends only on 001 (the new
`UnitStatSheet` fields existing with neutral defaults).

## Steps

1. Open each existing `UnitStatSheet` asset (player and any mob-specific
   sheets) in the Unity inspector. Confirm the new "Duration Skill Energy"
   header shows `Base Energy Gain = 0`, `Increased Energy Gain Percent = 0`,
   `Energy Gain Multiplier = 1` — Unity should have applied these via the new
   field initializers automatically; no manual fix-up should be needed.
2. If any interval-spawning skill loadout is equipped, verify in Play Mode
   (or via the existing EditMode tests from task 003) that duration-skill
   child-spawn cadence is visually/numerically unchanged from before this
   plan — the whole point of the neutral defaults is zero behavior change
   until the user opts in.
3. Optionally, author non-neutral values on the player's `UnitStatSheet` to
   actually grant the new bonus (e.g. a small `baseEnergyGain` or
   `increasedEnergyGainPercent` as a baseline player stat, or reserve the
   fields for a future passive/item system to write into). This is a
   content-balance decision left to the user — no default recommendation is
   made here.

## Acceptance Criteria

- All existing `UnitStatSheet` assets open without errors and show the new
  fields at their neutral defaults.
- Any equipped interval-spawn skill's observed child-spawn rate is unchanged
  pre/post this plan when the new fields are left at neutral.

## Dependencies

Depends on 001. Independent of 002-004 for verification purposes, though in
practice this is done last since it's the final sanity check on the whole
change.

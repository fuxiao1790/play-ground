# 007 — Unity Editor asset fixup (user-performed)

**This task is instructions for the user, not something the agent executes.**
Per [[editor-steps-are-user-steps]], Unity asset/YAML repair goes through the
Editor, not hand-edited files.

## Why

Deleting `ProjectileIntervalSpawnTrigger.cs` and `AoeIntervalSpawnTrigger.cs`
(task [002](./002-merge-trigger-class.md)) orphans the four existing trigger
assets that reference those classes' script GUIDs:

- `Assets/ScriptableObjects/Skills/Triggers/ProjectileIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Skills/Triggers/ProjectileIntervalSpawnTrigger2.asset`
- `Assets/ScriptableObjects/Skills/Triggers/AoeIntervalSpawnTrigger.asset`
- `Assets/ScriptableObjects/Skills/Triggers/AoeIntervalSpawnTrigger2.asset`

Opening the project after the code change lands will show these four as
"Missing Script" in the Inspector.

## Steps (per asset, all four)

1. Select the asset in the Project window
   (`Assets/ScriptableObjects/Skills/Triggers/`).
2. In the Inspector, the Script field will show "Missing (Mono Script)".
   Drag the new `IntervalSpawnTrigger` script (`Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`)
   onto that field — or use the small circle picker next to the field and
   select `IntervalSpawnTrigger` from the list.
3. Unity re-binds the asset to the new script and matches serialized field
   values by name. Every field on the four existing assets
   (`manaCostMultiplier`, `manaCostIncreased`, `energyPerSecond`,
   `projectileCount`/`sideSpreadDegrees` or `echoCount`/`scatterRadius`,
   `displayName`, `assetGuid`) has an identically-named field on the merged
   class, so existing values should carry over automatically. Confirm each
   asset's values in the Inspector still match what they were before (listed
   below for reference) — if any field reads back as a default instead of its
   old value, that one needs re-entering by hand.

## Expected values per asset (for verification, from current `.asset` contents)

| Asset | energyPerSecond | manaCostMultiplier | manaCostIncreased | Extra fields |
|---|---|---|---|---|
| `ProjectileIntervalSpawnTrigger.asset` | 25 | 1.1 | 1.1 | `projectileCount=1`, `sideSpreadDegrees=15` |
| `ProjectileIntervalSpawnTrigger2.asset` | 35 | 1.1 | 1.1 | `projectileCount=2`, `sideSpreadDegrees=15` |
| `AoeIntervalSpawnTrigger.asset` | 25 | 1.1 | 1.1 | `echoCount=1`, `scatterRadius=3` |
| `AoeIntervalSpawnTrigger2.asset` | 35 | 1.1 | 1.1 | `echoCount=2`, `scatterRadius=4` |

## Optional cleanup (not required)

- The four `.asset` file names still say "Projectile"/"Aoe" even though the
  class is now generic — renaming them (e.g. to
  `IntervalSpawnTrigger_ProjectileBurst.asset` or similar) is a pure
  organizational choice, not required for anything to function. Leave as-is
  unless it's confusing in the trigger catalog UI.
- No `TargetedIntervalSpawnTrigger.asset` ever existed, so there's nothing to
  fix up for the targeted case — but a new asset instance of
  `IntervalSpawnTrigger` can now be authored and, by filling in `echoCount`
  and leaving `projectileCount`/`sideSpreadDegrees` at their defaults, used
  as a targeted-effect interval trigger — something the old three-class setup
  required a dedicated `TargetedIntervalSpawnTrigger` asset for.

## Acceptance Criteria

- All four assets show a valid, non-missing `IntervalSpawnTrigger` script in
  the Inspector.
- Values match the table above (or have been manually re-entered to match).
- No console errors on project load referencing these assets.
- Any `SkillUiTriggerCatalog` asset listing these four by reference still
  resolves them (spot-check the catalog's Inspector list still shows named
  entries, not "None").

## Dependencies

Requires [002](./002-merge-trigger-class.md) to have landed (the new script
must exist to re-point to). Independent of 001, 003-006.

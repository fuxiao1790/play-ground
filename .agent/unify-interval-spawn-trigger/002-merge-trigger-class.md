# 002 — Merge the three trigger subclasses into one concrete class

## Scope

`Assets/Scripts/Skills/Trigger/IntervalSpawnTrigger.cs`,
`Assets/Scripts/Skills/Trigger/ProjectileIntervalSpawnTrigger.cs` (+ `.meta`),
`Assets/Scripts/Skills/Trigger/AoeIntervalSpawnTrigger.cs` (+ `.meta`),
`Assets/Scripts/Skills/Trigger/TargetedIntervalSpawnTrigger.cs` (+ `.meta`).

## Change

In `IntervalSpawnTrigger.cs`:
- Remove `abstract` from the class declaration.
- Add `[CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Interval Spawn", fileName = "NewIntervalSpawnTrigger")]`.
- Add the four fields, carried over verbatim (same names, same attributes) from
  the three deleted subclasses:
  ```csharp
  [Min(0)] public int projectileCount;
  [Range(0f, 180f)] public float sideSpreadDegrees = 30f;
  [Min(0)] public int echoCount;
  [Min(0f)] public float scatterRadius;
  ```
- Add the tag overrides. `SourceSkillTags` now reads the `Interval` capability
  flag added in [001](./001-add-interval-tag.md) instead of the old
  `Projectile | Aoe` shape check — this is what lets a pulse `AoeSkill` (tagged
  `Aoe` only, no `Interval`) fail source validation as a hard Error in
  [004](./004-update-validator.md), the same way a `TargetedSkill` source
  already would. `TargetSkillTags` is unioned across all three former
  subclasses:
  ```csharp
  public override SkillDefinitionTags SourceSkillTags => SkillDefinitionTags.Interval;
  public override SkillDefinitionTags TargetSkillTags => SkillDefinitionTags.Any;
  ```
- Keep the existing `energyPerSecond` field and `ResolveEnergyPerSecond` /
  `ManaToEnergyCost` methods unchanged.

Delete `ProjectileIntervalSpawnTrigger.cs`, `ProjectileIntervalSpawnTrigger.cs.meta`,
`AoeIntervalSpawnTrigger.cs`, `AoeIntervalSpawnTrigger.cs.meta`,
`TargetedIntervalSpawnTrigger.cs`, `TargetedIntervalSpawnTrigger.cs.meta`.

Do not touch any `.asset` file — see [007](./007-editor-asset-fixup.md).

## Acceptance Criteria

- `IntervalSpawnTrigger` is a concrete, creatable ScriptableObject carrying
  `energyPerSecond`, `projectileCount`, `sideSpreadDegrees`, `echoCount`,
  `scatterRadius`.
- `SourceSkillTags` is exactly `SkillDefinitionTags.Interval`.
- The three deleted subclass files no longer exist anywhere in the repo
  (`.cs` and `.cs.meta`).
- No other `.cs` file still references `ProjectileIntervalSpawnTrigger`,
  `AoeIntervalSpawnTrigger`, or `TargetedIntervalSpawnTrigger` by name (this
  will be false until 003/004/005 land — that's expected; this task just
  establishes the new type).

## Dependencies

Requires [001](./001-add-interval-tag.md) (needs `SkillDefinitionTags.Interval`
to exist).

## Scope/Complexity

Small. Single file edit + three file deletions (plus their `.meta`s).

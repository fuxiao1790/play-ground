# 006 — Update documentation

## Scope

- `Docs/reference/game-logic/skill-system.md`
- `Docs/folder-structure.md`
- `Docs/reference/simulation/targeted-system.md`

## Change

### `skill-system.md`

Rewrite the **Trigger Types** section (currently three separate blocks:
`ProjectileIntervalSpawnTrigger` ~478-497, `AoeIntervalSpawnTrigger` ~498-517,
`TargetedIntervalSpawnTrigger` ~605-621) into one `IntervalSpawnTrigger` block
documenting:
- all four fields (`projectileCount`, `sideSpreadDegrees`, `echoCount`,
  `scatterRadius`) and that only the pair matching the compiled effect's
  actual type is used — the other pair is inert;
- `SourceSkillTags = Interval` (changed from `Projectile | Aoe`), and what
  that means: a new `SkillDefinitionTags.Interval` flag (added in
  [001](./001-add-interval-tag.md)) marks skills that have a duration to
  accrue energy over — `ProjectileSkill` and `LingeringAoeSkill` only. Pulse
  `AoeSkill` and `TargetedSkill` don't carry it, so they fail source
  validation the same generic way any other tag mismatch would, now at Error
  severity instead of the old bespoke pulse-AOE warning;
- `TargetSkillTags = Any` (changed from three separate single-tag values);
- that which `Runtime*IntervalSpawnSetup` gets built (and onto which field)
  is decided by the compiled effect's runtime type, not by any authored
  choice — there is no longer a "wrong" effect type for this trigger, short
  of the effect failing to compile to any spawnable definition at all.

Update the **"Energy-driven source/child support" table** (~520-524) — the
three-column table (`ProjectileIntervalSpawnTrigger` / `AoeIntervalSpawnTrigger`
/ `TargetedIntervalSpawnTrigger`) collapses since one trigger now covers all
three columns:

```
| Source / Child | Any child type |
|---|---|
| Projectile source | `IntervalSpawnTrigger` |
| Lingering AOE source | `IntervalSpawnTrigger` |
| Pulse AOE or targeted source | error, no-op |
```

(Note: "warning, no-op" becomes "error, no-op" per [004](./004-update-validator.md)
— validation severity changed, the no-op compile behavior did not.)

Update the reference to `TargetedIntervalSpawnTrigger` in the **targeted
chains** section (~349, "put a `TargetedIntervalSpawnTrigger` on a projectile
or lingering AOE source") to say `IntervalSpawnTrigger`.

Update the **Validation Warnings** list (~711-733): the line "interval trigger
source is a pulse AOE instead of a projectile or lingering AOE" should note
this is now Error severity (blocks the affected spawn per the doc's own
severity convention), not a soft warning, and should mention it's produced by
the generic `UnsupportedTriggerSource` tag-mismatch check rather than a
dedicated interval-specific check.

Leave `IntervalSpawnTrigger.ResolveEnergyPerSecond` references (~536, and the
`numeric-modifiers.md` mention) as-is — that method already lives on the base
class being promoted to concrete, no doc change needed there.

### `folder-structure.md`

Line 231 currently lists `TargetedIntervalSpawnTrigger.cs`: targeted trigger
links` alongside (presumably adjacent) entries for the other two. Replace the
three entries with one line for `IntervalSpawnTrigger.cs` describing it as the
single interval-spawn trigger link (energy-driven, targets any compiled
projectile/AOE/targeted effect).

### `targeted-system.md`

Line 16 references `TargetedIntervalSpawnTrigger off a projectile or
lingering AOE`. Update to `IntervalSpawnTrigger`.

## Acceptance Criteria

- No doc under `Docs/` still names `ProjectileIntervalSpawnTrigger`,
  `AoeIntervalSpawnTrigger`, or `TargetedIntervalSpawnTrigger`.
- The rewritten `skill-system.md` section accurately describes
  dispatch-by-compiled-child-type (matching [003](./003-collapse-compiler-dispatch.md))
  and the `Interval` tag's role in source validation (matching
  [001](./001-add-interval-tag.md) and [004](./004-update-validator.md)), not
  dispatch-by-trigger-subtype or the old ad-hoc pulse-AOE check.

## Dependencies

Content depends on the design decided in 001-004 (can be drafted in parallel
once those are settled, but should be proofread against the final code before
this task is marked done).

## Scope/Complexity

Small–medium. Mostly prose; the trigger-types section is the main rewrite,
the rest are search-and-replace-level edits.

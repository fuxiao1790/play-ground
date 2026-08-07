# 004 — Docs And Tests

**Depends on:** 001–003. **Scope:** small.

## Docs

`Docs/reference/simulation/targeted-system.md`:

- **Spawn And Resolve** — state that a cast chain's first target is acquired by
  `ExternalSpawnGateSystem` from the aim point, bounded by `chainDistance`, and that the event's
  `AcquireAnchor` is therefore the target position, not the raw cursor. Interval-child and on-hit
  chains still anchor on their source/impact position and acquire in resolve (D2).
- **Render Mirror And VFX** — replace the current "spawned armed regardless" rule with the
  conditional one from 003: armed only when nothing was acquired.
- Note that fork *i* still ranks off the anchor, so multi-chain casts keep distinct forks (D3).

`Docs/contracts/spawn-events-and-commands.md`: add `HasAcquiredTarget` to the targeted event and
command field lists.

## Tests

`TargetedSpawnPipelineEditModeTests` — pipeline-level, no gate:

- Command with `HasAcquiredTarget = 1` → entity materialises unarmed with `LinkTarget` and
  `kinematics.Position` on the anchor.
- Command with `HasAcquiredTarget = 0` → entity materialises armed, as today. Replaces
  `SpawnFrame_ArmsEveryChainSoNoSpriteDrawsOnTheCaster`, which encodes the unconditional rule.

New `TargetedAcquisitionEditModeTests` (or extend the resolve tests) — gate-level:

- Nearest hostile inside `chainDistance` of the anchor is picked; a nearer same-faction proxy is
  skipped (the faction rule must survive the extraction — N1).
- Nothing in range → no acquisition, anchor untouched.
- Acquisition and hop selection agree: the target `TargetedAcquisition` returns for an anchor is
  the same one resolve's link 0 picks for that anchor.

`TargetedResolveEditModeTests`:

- Assert link 0's `LineSegment` starts at `chain.Origin` when `LinkTarget` was seeded away from
  it — the regression guard for the `LinkSource` line in 003.

## Acceptance criteria

- Full EditMode suite green; user runs it and supplies the XML per project rule.
- PlayMode `TargetedSkillPlayModeTests` green — cast-to-damage frame count is unchanged by this
  plan, so any movement there is a real regression.

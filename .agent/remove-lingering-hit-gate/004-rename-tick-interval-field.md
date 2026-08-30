---
name: rename-tick-interval-field
description: Rename AoeSpawnCommand.RepeatHitCooldownSeconds to TickIntervalSeconds to match the naming already used one layer up the pipeline, now that only VFX cadence consumes it.
---

# 004 - Rename the Surviving Tick-Interval Field

## Depends On

Independent of tasks 001-003; can land in any order relative to them, but
must land before task 006 (docs) references the new name.

## Changes

Rename `RepeatHitCooldownSeconds` → `TickIntervalSeconds` on
`AoeSpawnCommand` ([AoeSpawnPipeline.cs:71](../../Assets/Scripts/System/Aoes/AoeSpawnPipeline.cs#L71))
and update every producer/consumer:

- [AoeSpawnApplySystem.cs:656](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L656) — `PulseVfxFor`: `cmd.RepeatHitCooldownSeconds` (both occurrences on that line) → `cmd.TickIntervalSeconds`.
- [AoeSpawnApplySystem.cs:664](../../Assets/Scripts/System/Aoes/AoeSpawnApplySystem.cs#L664) — `VfxTimingFor`: same rename.
- [SkillDriver.cs:1489](../../Assets/Scripts/Skills/SkillDriver.cs#L1489) — `BuildAoeTemplate`: `RepeatHitCooldownSeconds = child.TickIntervalSeconds,` → `TickIntervalSeconds = child.TickIntervalSeconds,` (the right-hand side, `child.TickIntervalSeconds`, is a different, already-correctly-named field on `RuntimeAoeDefinition` — do not touch it).
- [CombatRoot.cs:732](../../Assets/Scripts/System/Core/CombatRoot.cs#L732) — `RepeatHitCooldownSeconds = request.TickIntervalSeconds,` → `TickIntervalSeconds = request.TickIntervalSeconds,` (same note: only the left-hand side changes).
- [AoeSimulationTests.cs:1396](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1396), [:1455](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1455), [:1522](../../Assets/Tests/PlayMode/AoeSimulationTests.cs#L1522) — update the `AoeSpawnCommand` field-initializer name; the `tickInterval` local parameter names in `SpawnCircle` and its callers are already correctly named and don't change.
- `AoePlayModeTests.cs` (~line 254, ~line 1009) — same field-initializer rename in `AoeSpawnCommand` literals used for template-hash/registry tests.

**Explicitly not touched by this task** (confirmed correctly named already,
per index.md's grounding): `LingeringAoeDefinition.tickIntervalSeconds`
(Inspector-authored field), `RuntimeAoeDefinition.TickIntervalSeconds`,
`AoeSpawnRequest.TickIntervalSeconds` / `ProjectileAoeSpawnRequest`. Do not
rename these — only `AoeSpawnCommand`'s field is mismatched.

## Acceptance Criteria

- `AoeSpawnCommand` has no field named `RepeatHitCooldownSeconds`; it's
  `TickIntervalSeconds`.
- No behavior change: `PulseVfxFor`/`VfxTimingFor` compute identical values
  from the renamed field: only the identifier changed.
- `SpawnTemplateHash.Of(in AoeSpawnCommand)` produces identical hashes for
  identical command content before and after this rename (hash is over
  values/bytes, not field names — verify by spot-checking a registry-dedup
  test still passes with unchanged expectations).
- No remaining reference to `AoeSpawnCommand.RepeatHitCooldownSeconds`
  anywhere in the repo.

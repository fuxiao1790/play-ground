# 004 - Update Tests And Docs

## Goal

Align test archetypes and documentation with trail-free projectiles, then prove
projectile gameplay and targeted LineSegment links still work.

## Changes

1. Remove `ProjectileTrailVfxComponent` from manual projectile archetypes in:
   - `Assets/Tests/EditMode/CombatPoolCleanupSystemTests.cs`
   - `Assets/Tests/PlayMode/CombatPoolCleanupSystemTests.cs`
   - `Assets/Tests/PlayMode/ProjectileContinuousSimulationTests.cs`
   - `Assets/Tests/PlayMode/ProjectileSpawnPipelineTests.cs`
2. In PlayMode `CombatPoolCleanupSystemTests`:
   - Remove VFX dispatcher creation from movement-only helper.
   - Keep `ActiveProjectileContinuesToSimulateAfterDisabledPoolTrim` as
     regression that movement works without VFX singleton.
3. Remove now-unused VFX imports only where no remaining fixture/system needs
   them. Do not remove VFX dispatch setup from broader harnesses that still run
   AOE/arming/lifetime producers requiring queue owner.
4. Update documentation:
   - `Docs/reference/simulation/projectile-system.md`: remove trail component,
     movement emission, frame-order wording, and authoring description.
   - `Docs/reference/game-logic/skill-system.md`: remove projectile trail slot
     and registration paragraph; retain unrelated AOE/targeted VFX description.
   - `Docs/reference/simulation/vfx-system.md`: remove projectile producer and
     authoring/pacing statements; describe targeted links as sole current
     LineSegment producer.
   - `Docs/contracts/vfx-requests.md`: remove projectile LineSegment emission;
     retain targeted request contract.
5. Final static review:
   - No projectile trail symbols or serialized fields remain in production,
     tests, prefabs, or current docs.
   - `LineSegment` references remain only in shared VFX infrastructure,
     targeted feature, targeted content/tests, and generic VFX tooling/docs.

## Acceptance Criteria

- Test worlds compile with reduced projectile archetypes.
- Projectile movement no longer needs VFX singleton.
- Existing projectile spawn, reuse, arming, movement, continuous collision, and
  pool-cleanup behavior remains covered.
- Targeted chain links still enqueue and dispatch LineSegment requests.
- Docs describe one current LineSegment producer: `TargetedResolveSystem`.
- No documentation promises projectile trail authoring or distance-gated
  segment behavior.

## Validation Requested From User

Agents do not run tests. User runs these and exports XML:

### EditMode

Export `Logs/TestResults-EditMode-RemoveProjectileTrails.xml`:

- `PlayGround.Tests.PlayMode.ProjectileSpawnPipelineTests` (EditMode assembly)
- `PlayGround.Tests.EditMode.AgentVfxReadCompatibilityTests`
- `PlayGround.Tests.EditMode.CombatVfxRootRegistrationTests.LineSegmentId_RetainsShapeAndLocalIndex`
- `PlayGround.Tests.EditMode.TargetedResolveEditModeTests.Resolve_EmitsLineSegmentForEachLandedLink`
- `PlayGround.Tests.EditMode.TargetedResolveEditModeTests.Resolve_DropsLinkVfxIdWithNonLineSegmentShape`

### PlayMode

Export `Logs/TestResults-PlayMode-RemoveProjectileTrails.xml`:

- `PlayGround.Tests.PlayMode.ProjectileSpawnPipelineTests`
- `PlayGround.Tests.PlayMode.ProjectileContinuousSimulationTests`
- `PlayGround.Tests.PlayMode.CombatPoolCleanupSystemTests`
- `PlayGround.Tests.PlayMode.TargetedSkillPlayModeTests.LinkVfx_QueuesOneLineSegmentForEachResolvedLink`

### Manual editor check

- Open affected projectile prefabs and confirm no missing serialized references
  or trail controls.
- Run one MagicBolt/FireArrow scenario: projectile sprite, motion, collision,
  damage, and lifetime work; no segment trail appears.
- Run targeted Plague link scenario: link VFX still appears.

Review both XML files before claiming automated tests pass.

## Dependencies

- Depends on tasks 001-003.

## Scope / Complexity

Medium. Mostly fixture/doc cleanup, with regression coverage protecting shared
targeted LineSegment behavior.


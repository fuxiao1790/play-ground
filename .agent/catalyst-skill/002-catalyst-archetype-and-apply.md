# 002 — Catalyst archetype and refresh-or-add apply

## Goal

A catalyst body exists in ECS: it materializes from a cast, holds its group
identity, counts down a shared duration, renders, pools, and dies. Still
motionless (task 003) and still un-hittable (tasks 004-006).

## Changes

`Assets/Scripts/System/Catalysts/CatalystEcsComponents.cs` (new)
- `CatalystTag`.
- `CatalystIdentityComponent { CombatFaction Faction; int CatalystId; int TypeId; SkillSoundIds SoundIds; float SpawnSoundRadius; }`
- `CatalystOwnerComponent { Entity Owner; Hash128 TemplateKey; byte DespawnOnOwnerLoss; }`
  — `(Owner, TemplateKey)` is the group key; `CatalystId` orders members.
- `CatalystTriggerComponent { OnHitSpawnRef OnHitSpawn; CatalystTriggerAim Aim; }`
- `CatalystMotionComponent` — declared here, fields owned by task 003.
- Each type gets the `// ECS Lifecycle:` comment the other domains carry.

Archetype (built once in the apply system):
`CatalystTag`, `CatalystIdentityComponent`, `CatalystOwnerComponent`,
`CatalystTriggerComponent`, `CatalystMotionComponent`,
`CombatKinematicsComponent`, `CombatCollisionComponent`,
`CombatCollisionActiveTag`, `CombatLifetimeComponent`, `Active`, `ArmingTag`,
`CombatArmingComponent`, `CombatRenderComponent`, `CombatRenderAuthoring`,
`CombatRenderKindId`.

Deliberately absent, and a reviewer should treat their appearance as a defect:
`CombatHitPayload` (no damage, no crit, no stacks), `ProjectileContactGateElement`
(the gate lives on the projectile), `TimedSpawnComponent`, `Health`,
`TargetStackEntry`.

`Assets/Scripts/System/Catalysts/CatalystSpawnApplySystem.cs` (new, `SystemBase`)
- Runs in `SimulationSystemGroup`, `[UpdateAfter(typeof(ExternalSpawnGateSystem))]`,
  `[UpdateBefore(typeof(CatalystAnchorSystem))]`.
- Reads `CatalystSpawnTemplate` directly (throw if missing).
- Per drained `CatalystSpawnEvent`:
  1. Collect live members matching `(Owner, TemplateKey)` — a `CatalystTag` +
     `Active` query, tiny by design.
  2. Set every member's `CombatLifetimeComponent.Remaining = template.DurationSeconds`.
  3. If `members < template.MaxCount`, claim a disabled slot
     (`WithDisabled<Active>()` + `CatalystTag`) or cold-create the archetype,
     write all component data from the template, assign the next monotonic
     `CatalystId`, and emit `SpawnTemplateRefEmit.AcquireCatalyst`.
  4. Respace: members sorted by ascending `CatalystId` get
     `Phase = index / count * 2π`.
- At max count, step 3 is skipped; the refresh in step 2 is the whole effect of
  the cast, which is the decided overflow behavior and covers the oldest member.
- `CatalystId` comes from a persistent `NativeReference<int>` counter on the
  system; reject wrap. Slot reuse resets lifetime, arming, phase, trigger ref,
  render data, and collision bounds — nothing may survive from the previous
  occupant.
- Main-thread structural work is correct here: casts are cooldown-gated and rare.

`Assets/Scripts/System/Lifetime/CombatLifetimeSystem.cs`
- Add `CatalystLifetimeJob` beside the three existing jobs (same system, not a
  new one): count down, on expiry `CombatDeathUtility.Kill(active, collisionActive, arming)`
  and `SpawnTemplateRefEmit.ReleaseCatalyst`.

`Assets/Scripts/System/Lifetime/CombatPoolCleanupSystem.cs`
- Own `CatalystTag` query for trimming, like the targeted pool, so catalyst
  slots do not compete with projectile/AOE headroom.

## Acceptance criteria

- First cast creates one body with full duration; three more casts under a
  `maxCount` of 4 leave 4 bodies with equal `Remaining`.
- A cast at max count adds nothing and refreshes every member's `Remaining`.
- Phases after each add are evenly spaced and ordered by `CatalystId`.
- Two casters casting the same catalyst skill keep separate groups; the same
  caster with two different catalyst skills keeps separate groups.
- Expiry disables `Active` and emits exactly one release delta per body.
- A reused slot carries no state from its previous occupant.
- No catalyst entity has `CombatHitPayload`.

## Tests to run (PlayMode, `CatalystApplyTests`)

- `FirstCast_CreatesOneBodyWithFullDuration`
- `RepeatCasts_AddUpToMaxCount`
- `CastAtMaxCount_RefreshesAllAndAddsNone`
- `AddMember_RespacesPhasesByCatalystId`
- `SeparateOwnersOrTemplates_KeepSeparateGroups`
- `Expiry_DisablesActiveAndReleasesTemplateKey`
- `ReusedSlot_ResetsAllPerInstanceState`

## Dependencies

001.

## Scope

Medium. The apply system is the only genuinely new control flow; the rest is
archetype declaration and two small additions to existing lifetime/pool systems.

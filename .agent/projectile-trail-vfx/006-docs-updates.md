# 006 — Documentation Updates

## Scope

Several docs currently assert, as settled fact, that projectiles never emit
VFX/LineSegment requests. Once 001–005 land those statements become false and
must be corrected so the docs stay trustworthy references
(`Docs/project-overview.md`: "these are design references... should be checked
against code before implementation work" cuts both ways — code changes must
walk the doc back to true, too).

## Changes

### `Docs/contracts/vfx-requests.md`

Current text (lines 11–14):

> `LineSegmentVfxSpawn` carries a directional start point, end point, and
> width. The dispatcher routes them through the Circular, TimedCircular, and
> LineSegment data shapes. AOE systems emit the circular shapes;
> `TargetedResolveSystem` emits circular hit/expire requests plus one
> `LineSegmentVfxSpawn` per resolved chain link, and is the only `LineSegment`
> producer. Projectile systems do not emit these requests.

Replace the last two sentences with something like:

> `TargetedResolveSystem` emits circular hit/expire requests plus one
> `LineSegmentVfxSpawn` per resolved chain link. `ProjectileMovementSystem`
> emits one `LineSegmentVfxSpawn` per active, non-arming projectile each time
> it has travelled at least its authored `StepDistance` since the last
> emitted segment (not once per frame), when that projectile's authored trail
> VFX id is nonzero. These are currently the only two `LineSegment` producers.

### `Docs/reference/simulation/vfx-system.md`

- Line 75, `LineSegment` shape summary: remove "directional graph placeholder;
  current AOE emitters do not produce this shape" (or rephrase — AOE still
  does not produce it; projectiles now do, so the line should not read as if
  the shape is unused).
- "Emitters" section (~lines 171–188): add `ProjectileMovementSystem`
  (distance-gated: one segment per authored `StepDistance` of travel, per
  trailed projectile) to the list of `LineSegment` producers alongside
  `TargetedResolveSystem`.
- "Authoring" section (~lines 204–209): the sentence "Targeted chains are its
  only producer... No AOE producer emits this shape" needs to become
  "Targeted chains and projectile trails are its producers; no AOE producer
  emits this shape," and should mention `BasicAttackPrefab`'s trail slot
  alongside `TargetedPrefab`'s link slot as the two LineSegment authoring
  points.

### `Docs/reference/simulation/projectile-system.md`

- "Rendering" section: remove/replace "Projectiles do not emit AOE VFX
  requests. Their presentation path is the batched sprite renderer described
  above." with a note that projectiles now optionally emit a distance-gated
  `LineSegment` trail VFX request via `ProjectileMovementSystem`, in addition
  to the batched sprite renderer (the two are independent presentation paths —
  the sprite always renders; the trail is opt-in per prefab and paced by
  authored `StepDistance`, not by frame).
- "Entity Archetypes And Reuse" list: add `ProjectileTrailVfxComponent` to the
  list of components both lanes carry.
- "Main Files" list: add a line for `ProjectileTrailVfxComponent`'s home
  (`ProjectileEcsComponents.cs` is already listed — just extend its
  description) and note `ProjectileMovementSystem`'s new VFX-producer role in
  its existing bullet.
- "Authoring Notes" section: add a bullet noting `BasicAttackPrefab`'s trail
  VFX slot and that `SkillDriver` registers it the same way AOE/Targeted VFX
  slots are registered.
- "Current Frame Order" step 5 (`ProjectileMovementSystem` bullet): note it now
  also produces `LineSegmentVfxEvent`s, consistent with step 7's existing
  "collision systems emit damage and spawn events" phrasing style.

### Optional: `Docs/reference/game-logic/skill-system.md`

Check this file's existing description of projectile authoring fields (it is
listed as referencing `BasicAttackPrefab` in the codebase) and add the trail
slot if that doc enumerates AOE/Targeted VFX slots in the same place — read it
during implementation to decide whether it needs the same treatment as
`vfx-system.md`'s Authoring section.

## Acceptance Criteria

- No doc under `Docs/` asserts that projectiles never emit VFX or
  `LineSegment` requests after this task.
- `Docs/reference/simulation/vfx-system.md`'s Emitters/Authoring sections and
  `Docs/contracts/vfx-requests.md` list exactly two `LineSegment` producers:
  `TargetedResolveSystem` and `ProjectileMovementSystem`.
- Doc wording distinguishes "no trail authored → no emission" (per-prefab
  opt-in) from "projectiles never emit" (no longer true) — a reader should
  come away understanding this is authored per prefab, not automatic for every
  projectile.

## Dependencies

Depends on 001–004 being settled (this task documents the final shape; if any
earlier task's design changes during implementation, update this task's
specifics to match before writing the doc prose).

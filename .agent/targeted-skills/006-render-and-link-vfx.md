# 006 — Render integration and link VFX

**Depends on:** 004. **Scope:** small. **Risk:** low.

## Why

Two visuals with very different weight. The `LineSegment` VFX is what players see. The sprite is a
debug and authoring affordance — requirements §5.1 — and the standing rule is: if a sprite
requirement would need a new render system, drop the requirement instead.

## Changes

`Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`

- Add `TargetedTag` to `renderQuery`'s `WithAny<ProjectileTag, AoeTag>`. Both variants carry
  `TargetedTag`, so one addition covers them; `LingeringTargetedTag` is a pool discriminator the
  render query must not look at.
- Nothing else changes. `IgnoreComponentEnabledState` stays, so pooled targeted entities sit in the
  buffer as degenerate quads exactly as pooled projectiles and AOEs already do. Known and accepted
  (requirements decision 13).

Render mirror — already written by `TargetedResolveCore` in task 004, restated here as the
contract this task verifies:

- `CombatRenderComponent.AlignToVelocity = 1`, set at spawn.
- `kinematics.Position = chain.LinkTarget`, `kinematics.Velocity = LinkTarget - LinkSource`.
- `RenderTypeId = 0` when no sprite is authored — `ElementFor` already treats that as a degenerate
  instance, so a VFX-only chain needs no branch anywhere.
- **No stretching.** The sprite keeps its authored size and only faces the target. The render quad
  is centred on `Position`, so a span-the-link sprite would have to anchor at the segment midpoint,
  which contradicts rendering on the target and duplicates what `LineSegment` does better.

Link VFX — emitted by `TargetedResolveCore`, one call per landed link:

- `VfxEmit.EnqueueLineSegment(linkVfxId, LinkSource, LinkTarget, linkWidth, lineSegments)`.
- `VfxEmit` drops ids that do not decode to `VfxDataShape.LineSegment`, so a mis-authored id fails
  silently at runtime — task 013 catches it at authoring time instead.
- Optional circular impact flash at `LinkTarget` when `impactVfxId != 0`.
- Combine the resolve job handle into `CombatAoeVfxDispatchSingleton.ProducerHandle` on the main
  thread (C6).

`CombatVfxRoot` / `CombatAoeVfxDispatcher` — **no changes**. The `LineSegment` shape is already
wired end to end: request struct, `BucketLineSegmentVfxSpawnsJob`, `LineSegmentVfxResources`,
`DrainAndDispatchLineSegment`, and the GPU buffers. This feature is that lane's first gameplay
producer.

## Acceptance criteria

- EditMode: a chain with a registered sprite produces a render instance; with `RenderTypeId = 0` it
  produces a degenerate one and nothing is drawn.
- EditMode: after link k, the entity's `CombatRenderComponent` resolves to a matrix positioned at
  target k and oriented along `target k - target k-1`.
- EditMode: an update with no landed link leaves the pose unchanged (no snap to `+X`).
- EditMode: N landed links enqueue exactly N `LineSegmentVfxSpawn` with matching endpoints.
- EditMode: a link VFX id that decodes to a non-`LineSegment` shape enqueues nothing and does not
  throw.
- PlayMode (task 014 may host this): dispatched `LineSegment` count summed over a walk equals the
  resolved link count.
- Existing VFX shared-graph area-size regression tests still pass — this feature adds a producer to
  a shape that does not sample `AreaSizes`, but the suite must stay green.

## Notes

The debug sprite is why `Position` tracks `LinkTarget` and `Velocity` carries the link direction.
`Velocity` on a targeted entity is a **render direction, not motion** — nothing integrates it, and
targeted entities are absent from `ProjectileMovementSystem`'s query. Any future cross-domain
system reading `Velocity` must require a domain tag (C1).

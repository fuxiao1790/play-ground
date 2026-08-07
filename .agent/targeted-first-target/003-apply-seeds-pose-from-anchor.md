# 003 — Apply Seeds Pose From The Acquired Anchor

**Depends on:** 002. **Scope:** small — one apply job, one resolve line.

## Change

`TargetedSpawnApplySystem.TargetedSpawnJob`
([TargetedSpawnApplySystem.cs:167-195](../../Assets/Scripts/System/Targeted/TargetedSpawnApplySystem.cs#L167-L195)):

| Field | Today | After |
|---|---|---|
| `chain.Origin` | `cfg.Origin` | unchanged — the caster, still the bolt's start |
| `chain.LinkSource` | `cfg.Origin` | unchanged |
| `chain.LinkTarget` | `cfg.Origin` (placeholder) | `cfg.AcquireAnchor` |
| `kinematics.Position` | `cfg.Origin` | `cfg.AcquireAnchor` |
| `armingMask` | `true` (this session's flash fix) | `cfg.ArmSeconds > 0f \|\| cfg.HasAcquiredTarget == 0` |

An acquired chain is therefore visible on its spawn frame, standing on its first target. An
unacquired one stays hidden until its walk finds something, which is the current behaviour.

`TargetedResolveSystem.Walk` must stop deriving link 0's segment start from `LinkTarget`:

```csharp
chain.LinkSource = firstLink ? chain.Origin : currentPosition;
```

Today `LinkTarget == Origin` at spawn makes those identical; after the seed change they are not,
and without this line the first `LineSegment` would collapse to zero length on the target instead
of drawing from the caster.

## Constraints

- `chain.LinkSource` must remain the caster for link 0 — the caster-to-first-target bolt is the
  chain's signature visual and is unrelated to where the sprite sits.
- Do not touch the deferred-expiry rule added this session
  ([TargetedResolveSystem.cs:248-259](../../Assets/Scripts/System/Targeted/TargetedResolveSystem.cs#L248-L259));
  it is what renders the *last* link, and this task fixes the *first*.

## Acceptance criteria

- Acquired cast: entity materialises with `LinkTarget` and `kinematics.Position` on the acquired
  target, `ArmingTag` disabled (when `armSeconds` is 0), so render prep draws the sprite there on
  the spawn frame.
- Unacquired cast: armed, invisible, unchanged from current behaviour.
- Link 0 still emits its `LineSegment` from `chain.Origin` to the first target.
- `Resolve_EmitsLineSegmentForEachLandedLink` passes with the caster-origin start asserted
  explicitly rather than incidentally.

# 008 — Remove `CombatApplyBridge.HasAnyStatusRange`

## Why

`CombatApplyBridge` is the one system in this plan that **must stay managed**.
It resolves `TargetCompanion` and calls `ICombatTarget.ReceiveCombatTick`
(`Assets/Scripts/System/Presentation/CombatApplyBridge.cs:91-103`), which
`Docs/architecture/phase-order.md` §*Phase Ownership* and
`Docs/contracts/target-proxy.md` §*Restrictions* both reserve for presentation.
`Docs/coding-standards.md` §*Hybrid ECS/Scene Rule* names it explicitly as one
of only two systems permitted to read `TargetCompanion`. None of that changes.

What can go is a redundant pass. `OnUpdate` walks the results list twice:

```csharp
bool hasStatusSnapshots = HasAnyStatusRange(lane.Results);   // :42 — full pass
if (lane.Results.Length == 0 && !hasStatusSnapshots)
{
    lane.Clear();
    return;
}
// ...
ReplayCombat(lane.Results, lane.StatusSnapshots, EntityManager);  // :52 — full pass
```

`HasAnyStatusRange` (`:109-125`) scans every `CombatTickResult` looking for
`StatusCount > 0`.

## The pass is not just redundant — the condition is unreachable

Read the guard carefully:

```csharp
if (lane.Results.Length == 0 && !hasStatusSnapshots)
```

`HasAnyStatusRange` iterates `lane.Results`. If `lane.Results.Length == 0` the
loop body never executes and it returns `false` unconditionally. So
`hasStatusSnapshots` is **always** `false` whenever the first operand is true,
and the compound condition is exactly equivalent to:

```csharp
if (lane.Results.Length == 0)
```

The `HasAnyStatusRange` call cannot change the branch outcome in any state. It
is a full linear scan of the results list, every frame, whose result is
discarded.

This is worth stating precisely because it changes the nature of the task: this
is not a performance micro-optimisation with a behavior tradeoff to weigh. It is
dead code that happens to cost O(results) per frame.

## Scope

- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`

## Change

Delete the `HasAnyStatusRange` method (`:109-125`) and the local at `:42`.
Simplify the guard:

```csharp
if (!lane.Results.IsCreated)
{
    return;
}

if (lane.Results.Length == 0)
{
    lane.Clear();
    return;
}

using (Marker.Auto())
using (TickReplayMarker.Auto(lane.Results.Length))
{
    ReplayCombat(lane.Results, lane.StatusSnapshots, EntityManager);
}

lane.Clear();
```

`ReplayCombat` already skips results with neither hits nor status
(`:86-89`: `if (result.HitCount <= 0 && result.StatusCount <= 0) continue;`), so
no per-result filtering is lost.

### Do not also try to job-ify the replay

The obvious next thought — "the loop is managed, put it in a job" — is wrong
here. `ResolveTarget` calls `EntityManager.GetComponentObject<TargetCompanion>`
(`:136`) and the loop calls into a MonoBehaviour. Neither can enter a job. The
`statusScratch` copy (`:97-101`) into a managed `List<StatusStackSnapshot>`
exists because `ICombatTarget.ReceiveCombatTick` takes a managed list. Leave all
of it.

Volume is bounded by hit *targets* per frame, not hits — `Docs/reference/simulation/project-ecs-implementation.md`
§*Collision Event Dispatch* records that finalize already aggregates to one
`CombatTickResult` per target, so this loop is ≤ ~50 iterations
(`Docs/performance.md` target cap). It is not a hot path.

## Acceptance Criteria

- `HasAnyStatusRange` no longer exists.
- Behavior is unchanged in all three states: empty results, results with status,
  results without status.
- `lane.Clear()` is still called on every path that returns after reading the
  lane, including the empty-results early return — `CombatApplyFinalizeSingleSystem`
  re-fills these lists each frame via `applyResults.Clear()` (`:156`), but the
  bridge clearing them is what releases them for the next frame's producer.
- `PublishDropCount` (`:61-76`) still runs before the early return — it reports
  `StackEntryDrops` to the profiler counter and must not be skipped on an
  empty-results frame.
- The `ProducerHandle.Complete()` / reset at `:33-34` is untouched.

## Dependencies

None. Fully independent, smallest task in the plan.

## Scope/Complexity

Trivial. One method deleted, one condition simplified.

## Test Coverage

`Assets/Tests/EditMode/CombatHitDamageScaleEditModeTests.cs` and the collision
test suites exercise the replay path. Since this task is a provable no-op on
behavior, existing coverage is sufficient — any failure indicates the equivalence
argument above is wrong and should be investigated rather than worked around.

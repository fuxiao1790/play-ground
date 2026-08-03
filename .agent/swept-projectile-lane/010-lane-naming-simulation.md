# 010 — Lane Naming: Simulation

**Depends on:** 001–009 landed (all Complete)
**Scope:** medium — pure rename, no behavior change

## Goal

Make the two projectile collision lanes symmetrically named. Today one lane is the unnamed
default (`ProjectileCollisionSystem`) and the other is the special case
(`SweptProjectileCollisionSystem`) — the same default-plus-exception shape rejected at the
start of this design ("i don't want to simply name it fast projectile and projectiles").

## The rule

> `Projectile` + `{Discrete | Continuous}` + role — for anything that belongs to exactly
> **one** lane. **No lane word** for anything both lanes use.

Two consequences worth stating, because they are what the rule is for:

- `ProjectileMovementSystem`, `ProjectileSpawnExpansionSystem`, `ProjectileContactGateSystem`,
  `ProjectileTag`, `ProjectileIdentityComponent`, `ProjectileHitComponent` — **unchanged**.
  They serve both lanes, so they carry no lane word. Task 006 already established that
  movement is shared; the naming now says so.
- The lane word goes **after** `Projectile`, not before. Everything in
  `Assets/Scripts/System/Projectiles/` is `Projectile*`; `ContinuousProjectileCollisionSystem`
  would scatter the lane's files to `C` and `D` in a folder where every other file sorts under
  `P`. It also makes `grep Continuous` return the whole lane and nothing else.

`ProjectileTrackingSystem` keeps its name. It is discrete-only in practice, but only because
the continuous archetype does not carry `ProjectileTrackingComponent` — it is gated by a
component, not by a lane. Naming it `ProjectileDiscreteTrackingSystem` would claim a lane
check that is not in the code.

## Renames

### Files (`Assets/Scripts/System/Projectiles/`)

| Now | New |
|---|---|
| `ProjectileCollisionSystem.cs` | `ProjectileDiscreteCollisionSystem.cs` |
| `SweptProjectileCollisionSystem.cs` | `ProjectileContinuousCollisionSystem.cs` |
| `ProjectileSpawnApplySystem.cs` | `ProjectileDiscreteSpawnApplySystem.cs` |
| `SweptProjectileSpawnApplySystem.cs` | `ProjectileContinuousSpawnApplySystem.cs` |
| `SweptProjectileOriginSystem.cs` | `ProjectileContinuousOriginSystem.cs` |

**Move the `.cs.meta` alongside every `.cs`** — `git mv` both, in the same commit. A dropped
`.meta` makes Unity mint a fresh GUID on next import. None of these five are
`MonoBehaviour`/`ScriptableObject`, so nothing references them by GUID today and the damage
would be limited to a spurious asset-database churn, but there is no reason to take it.

### Types

| Now | New | Note |
|---|---|---|
| `SweptProjectileTag` | `ProjectileContinuousTag` | archetype discriminator |
| `ProjectileSweepComponent` | `ProjectileContinuousStepComponent` | field `Origin` unchanged |
| `ProjectileCollisionJob` | `ProjectileDiscreteCollisionJob` | |
| `SweptProjectileCollisionJob` | `ProjectileContinuousCollisionJob` | |
| `ProjectileSpawnJob` | `ProjectileDiscreteSpawnJob` | |
| `SweptProjectileSpawnJob` | `ProjectileContinuousSpawnJob` | |
| `SweptCandidate` | `HitCandidate` | `private` nested inside the continuous system — the lane word is redundant there |
| `CaptureOriginJob` | unchanged | already unprefixed, already nested |

`ProjectileContinuousStepComponent` rather than `…OriginComponent` so the parameter reads
`step.Origin` instead of `origin.Origin`:

```csharp
step.Origin = kinematics.Position;                       // capture
impactPoint = lerp(step.Origin, kinematics.Position, t); // collision
```

Rename the locals to match: parameter `sweep` → `step`, `SweepHandle` → `StepHandle`, the
`NativeArray` local `sweeps` → `steps`.

### Members

| Now | New | Where |
|---|---|---|
| `ProjectileSpawnEventSingleton.Commands` | `DiscreteCommands` | `ProjectileSpawnExpansionSystem.cs:26` |
| `ProjectileSpawnEventSingleton.SweptCommands` | `ContinuousCommands` | `:27` |
| `ProjectileExpansionJob.Commands` / `.SweptCommands` | `DiscreteCommands` / `ContinuousCommands` | `:182-183` |
| `CollisionConstants.MaxSweptHitsPerFrame` | `MaxContinuousHitsPerFrame` | `CollisionConstants.cs:31` |

Renaming `Commands` → `DiscreteCommands` is the half of this that is easy to skip and is the
whole point: leaving it as `Commands` next to `ContinuousCommands` rebuilds the
default-plus-exception shape one level down.

### Comments

`ECS Lifecycle:` comments on `ProjectileContinuousTag` and `ProjectileContinuousStepComponent`
name `SweptProjectileSpawnApplySystem` and `SweptProjectileOriginSystem`
([ProjectileEcsComponents.cs:49-67](../../Assets/Scripts/System/Projectiles/ProjectileEcsComponents.cs#L49-L67)).
Update them — `Docs/coding-standards.md` requires these to track the code they describe.

Prose in comments: **"swept lane" / "swept projectile" → "continuous lane" / "continuous
projectile"**. But "swept volume", "swept box", "the segment the projectile swept" describe
*geometry* and may stay; see the `CombatSweepMath` note below.

## `CombatSweepMath` stays

Recommendation: **do not rename** `CombatSweepMath`, `SupportExtent`,
`TryBuildTravelCorridor`, or `ClosestApproachParam`.

"Swept volume" is standard geometry vocabulary, not this codebase's lane vocabulary. The file
lives in `Api/Collision/Narrowphase/` beside `CombatCollisionMath`, is pure and
lane-agnostic, and `CombatContinuousCollisionMath` sitting next to `CombatCollisionMath` would
read as *the* collision math with an adjective — worse than what it replaces.

The distinction the rename encodes: **`Continuous` names the lane, `Sweep` names the geometry.**
Flagged in the index open questions so it is cheap to overrule.

## Not doing: a `ProjectileDiscreteTag`

Symmetric names do not make the *data* symmetric — absence of `ProjectileContinuousTag` still
means discrete, and `ProjectileDiscreteSpawnApplySystem._deadSlotQuery` still needs
`WithNone<ProjectileContinuousTag>`.

Adding a real `ProjectileDiscreteTag` was considered and rejected: it puts a component on the
highest-count archetype in the game, changes every discrete query, and changes the pooled
archetype (invalidating existing pool slots) — all to remove one `WithNone`. The asymmetry is
in the data on purpose. It is recorded here so nobody re-derives it after seeing the symmetric
names.

## Do this with the IDE's rename refactor

Not find/replace. `Swept` → `Continuous` does **not** produce these names — the lane word
changes position (`SweptProjectileCollisionSystem` → `ProjectileContinuousCollisionSystem`),
and `Commands` → `DiscreteCommands` has no `Swept` in it to match.

This matters more than usual because compile validation is still baseline-blocked by
`CombatTargetProxy.cs(381)` (`TargetProxyUpdateEvent`), so a half-applied rename will not be
caught by a build. Either fix that baseline error first, or drive every rename from
Rider/Visual Studio symbol rename, which is correct without compiling.

## Acceptance Criteria

- `grep -i swept` over `Assets/Scripts/System/` returns nothing.
- `grep Continuous` over `Assets/Scripts/System/Projectiles/` returns exactly the continuous
  lane; `grep Discrete` returns exactly the discrete lane.
- No file in `Assets/Scripts/System/Projectiles/` carries a lane word unless it belongs to
  exactly one lane. `ProjectileMovementSystem`, `ProjectileSpawnExpansionSystem`,
  `ProjectileContactGateSystem`, `ProjectileTrackingSystem` are unchanged.
- Every renamed `.cs` has its `.cs.meta` moved with it; `git status` shows renames, not
  add+delete pairs.
- `ProjectileSpawnEventSingleton` has no field named bare `Commands`.
- `git diff` contains **no** logic change — no altered conditions, no reordered statements, no
  changed literals. Identifiers, file names, and comment text only.
- `ECS Lifecycle:` comments name the systems that actually exist after the rename.

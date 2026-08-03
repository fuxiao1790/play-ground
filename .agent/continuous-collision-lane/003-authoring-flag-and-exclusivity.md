# 003 — Authoring Flag, Exclusivity, and the Compile-Time Tunneling Check

**Depends on:** 001
**Scope:** medium (touches GameLogic authoring, compiler, validator, and the ECS command shape)

## Goal

Three things, all decided at **skill-compile time** and never recomputed per spawn:

1. Carry an authored `continuousCollision` flag from the skill asset to `ProjectileSpawnCommand`.
2. Make sweep and tracking mutually exclusive — the combination is an **error that rejects
   the spawn**, not a silent degrade.
3. Warn when a **tracking-capable** projectile is fast enough to tunnel, because tracking
   forbids sweep, so that combination has no fix available at runtime.

## The authoring chain

Traced end to end; each hop needs the flag added:

| Hop | File | Change |
|---|---|---|
| 1. Authored asset | `Skills/SkillDefinition.cs:27` `ProjectileDefinition` | add `public bool continuousCollision;` |
| 2. Modifier pass | `Skills/Modifiers/BehaviorContexts.cs` | no new setter — sweep is not support-modifiable |
| 3. Compile | `Skills/SkillSetCompiler.cs:288-306` `BuildRuntime` | set the flag; run the exclusivity and tunneling checks here |
| 4. Runtime def | `Skills/Runtime/RuntimeProjectileDefinition.cs:39` | add `ContinuousCollision` and `SpawnBlocked` |
| 5. Spawn request | `System/Projectiles/ProjectileSpawnRequest.cs` | add optional ctor param + property |
| 6. Child config | same file, `ProjectileChildSpawnConfig` | add ctor param + property, for interval children |
| 7. Driver | `Skills/SkillDriver.cs` (incl. `:1062` child-config build) | pass through for direct, interval, and on-hit projectiles |
| 8. Template | `System/Core/CombatRoot.cs` template compile | copy onto the `ProjectileSpawnCommand` template |
| 9. Command | `System/Projectiles/ProjectileSpawnPipeline.cs` | add `public int ContinuousCollision;` |

**Command field type:** use `int` (0/1), matching the existing `HasTimedSpawner` convention in
the same struct ([ProjectileSpawnPipeline.cs:43](../../Assets/Scripts/System/Projectiles/ProjectileSpawnPipeline.cs#L43)).
Do not introduce a second convention for the same idea in one struct.

**Sweep is not support-modifiable.** No `SkillStat` entry, no `ProjectileBehaviorContext`
setter. It describes how collision is resolved, not a gameplay magnitude, and making it
modifiable would reintroduce the runtime-variable lane membership this design removed.

## Exclusivity: error and reject, do not silently drop

The conflict is reachable without anyone authoring it:
`ProjectileBehaviorContext.EnableTracking` ([BehaviorContexts.cs:41-46](../../Assets/Scripts/Skills/Modifiers/BehaviorContexts.cs#L41-L46))
lets a support turn tracking on during compilation, so a homing support socketed into a swept
skill produces the conflict from two individually-valid pieces of content.

**Detect at compile, in `SkillSetCompiler.BuildRuntime`** — the single point after modifiers
where both final values are known:

```csharp
if (p.continuousCollision && p.GetTrackingConfig().Enabled)
{
    // blocked: emit an Error-severity validation entry and mark the definition
    runtime.SpawnBlocked = true;
}
```

**Reject at fire time.** `SkillDriver` must not fire a slot whose compiled definition has
`SpawnBlocked`, and must route it through the existing rejection path so the player sees the
same behavior as any other rejected spawn — `slotStates[i].RefundFire()`, no mana spent, no
cooldown consumed ([SkillDriver.cs:144-162](../../Assets/Scripts/Skills/SkillDriver.cs#L144-L162)).

**Design note on where the rejection is raised.** The existing `SpawnRejectedEvent` →
`SpawnRejectionBridge` → `ICombatTarget.ReceiveSpawnRejected` lane exists for *dynamic*
rejections only ECS can decide, such as insufficient mana. This conflict is fully known at
compile time, so building an `ExternalSpawnRequest` and shipping it into ECS purely to be
rejected would be ceremony. `SkillDriver` raises the same refund locally instead, reusing
`ReceiveSpawnRejected`'s semantics without the round-trip.

Flag at review if you want the ECS lane used regardless — it is a two-line change to emit
into `SpawnRejectedSingleton` instead, and the player-visible result is identical.

## The tunneling check: can the projectile skip a gap in two ticks?

Runs in the compiler, **once per compiled skill**. Never in ECS, never per spawn.

The condition is "over two ticks of travel, does the projectile's footprint fail to overlap
what it could have hit" — two ticks rather than one so a single long or dropped frame is
covered:

```
travel     = speed * 2 * NominalTickSeconds          // NominalTickSeconds = 1/60
alongHalf  = min half-extent of the projectile shape // conservative: smallest footprint
gapFree    = 2 * (alongHalf + SmallestExpectedTargetRadius)

tooFast    = travel > gapFree
```

`alongHalf` uses the **minimum** half-extent rather than the extent along travel, because the
spawn rotation is not known at compile time and the minimum is the conservative choice
(smallest footprint, largest gap, warns earliest).

`SmallestExpectedTargetRadius` is an **editor-side lint constant**, default `0.35` (Bat,
`Prefabs/Mobs/Bat.prefab:193`). It is deliberately *not* a runtime value and must not be
readable from any simulation code — it exists only to make this warning meaningful. Without a
target size the check degenerates to "any gap at all", which fires for every projectile above
about `5 u/s` and is useless.

### Warn only when tracking is enabled

```csharp
if (tracking.Enabled && tooFast) { /* advisory warning */ }
```

This is the combination with **no available fix**: tracking forbids sweep, so the author
cannot resolve it by ticking `continuousCollision` — they must slow the projectile or drop the
homing. A fast projectile *without* tracking is not warned, because its fix is trivial and
obvious, and per the content decision on `MagicBolt`, skills will be authored for this
deliberately rather than nudged by a linter.

### Check against current content

`travel` at speed `s` is `s / 30`; `gapFree` for a `0.1 × 0.15` projectile is
`2 * (0.05 + 0.35) = 0.8`.

| Skill | Speed | travel | tooFast | tracking | warns |
|---|---|---|---|---|---|
| ArcaneMissile / 2 | 15 | 0.50 | no | off | no |
| MobArrowSkill | 16 | 0.53 | no | off | no |
| MagicBolt2 | 25 | 0.83 | yes | off | no |
| MagicBolt | 30 | 1.00 | yes | off | no |

Nothing warns today — the tracking restriction makes this purely forward-looking, which is
the intent. `MagicBolt` and `MagicBolt2` do cross the tunneling threshold, and that is a
known, accepted, authored state.

## Validation severity

`SkillValidationWarning` currently carries `Code`, `SlotIndex`, `Message`
([SkillValidationWarning.cs:16-33](../../Assets/Scripts/Skills/SkillValidationWarning.cs#L16-L33))
with no severity. Add a `SkillValidationSeverity Severity { get; }` defaulting existing codes
to `Warning`, so the blocking conflict can be distinguished from the advisory speed notice by
the loadout UI rather than by a lookup table of which codes happen to be fatal.

Two new `SkillValidationWarningCode` entries:

- `ContinuousCollisionCannotTrack` — **Error**. Blocks the spawn.
- `TrackingProjectileMayTunnel` — **Warning**. Advisory only.

Also add an `OnValidate` error on `ProjectileDefinition` for the directly-authored
`continuousCollision && trackingEnabled` case, per `Docs/coding-standards.md` (Fail Fast
Validation) — that one is a plain authoring mistake and should not require entering play mode.

## Acceptance Criteria

- `continuousCollision` reaches `ProjectileSpawnCommand` for **all four** spawn paths: direct cast,
  interval child, on-hit child, stack detonation.
- Sweep + tracking produces an `Error`-severity validation entry and a rejected spawn with a
  refunded fire; it never silently drops tracking and never spawns.
- The tunneling check runs in the compiler only. No speed/geometry math exists anywhere in
  the ECS spawn or collision path.
- `SmallestExpectedTargetRadius` is not reachable from `PlayGround.Sim`.
- The tunneling warning fires only when tracking is enabled.
- `OnValidate` errors on a directly-authored swept + tracking projectile.
- `ProjectileSpawnCommand` uses the `int` 0/1 convention already in the struct.
- No `SkillStat` or behavior-context setter is added for sweep.

## Risks

- **Missing a spawn path.** Interval and on-hit children build their commands through separate
  code from the direct cast. A dropped flag there produces a fast child projectile silently
  landing in the discrete lane — the exact bug this feature fixes. Task 008 tests all four.
- **Blocked skill feels broken.** A slot that refuses to fire with no UI feedback reads as a
  bug. The `Error` severity must actually surface in the loadout UI, not just in the warning
  list; confirm the UI renders it before calling this done.

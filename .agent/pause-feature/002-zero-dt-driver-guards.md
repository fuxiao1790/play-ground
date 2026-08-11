# 002 — Zero-dt Driver Guards

## Goal

Make the three sites that act without consulting dt inert on a zero-length
frame. This is the correctness core of the pause feature and is independent of
every other task — a driver that respects dt is pause-correct whether or not
`PauseController` exists.

## Scope

### `Assets/Scripts/Skills/SkillDriver.cs`

In `Tick`, add the guard **after** `ProcessPendingEdit()` and the
`compiledSlots == null` check, **before** the cooldown loop:

```csharp
ProcessPendingEdit();
if (compiledSlots == null) return;

float deltaTime = Time.deltaTime;
if (deltaTime <= 0f) return;   // frozen frame: no cooldown progress, no cast
```

Then feed the cached `deltaTime` into `slotStates[i].Tick(deltaTime)` rather
than reading `Time.deltaTime` again inside the loop.

Placement is load-bearing (I4). `ProcessPendingEdit` is the only path that
resolves queued loadout edits (`SkillDriver.cs:1201-1225`), and the loadout
stays usable while paused, so it must run above the guard. Everything below it
is cooldown progress and casting, neither of which may happen in zero elapsed
time.

This one change covers both the player and every mob: `MobRoot.cs:505` calls
`skillDriver.Tick(true, ...)` unconditionally, and the guard stops the cast
before the fire loop is reached.

### `Assets/Scripts/Spawn/ContinuousStreamBehaviour.cs`

In `Runtime.Tick`, return early when `dt <= 0f`, before the accumulator is
touched. The existing `Mathf.Max(0f, dt)` protects the accumulator from growing
but not the `while (accumulator >= 1f)` drain below it, which will still spawn
up to `maxSpawnsPerTick` mobs per frame whenever the accumulator is saturated.

### `Assets/Scripts/Player/PlayerVfxAura.cs`

Return early in `Update` when `Time.deltaTime <= 0f`, before the
`Time.time`-based emit check. Cosmetic leak (one emit), but it is the same class
of bug and the fix is one line.

## Not In Scope

`MobRoot.Update` and `PlayerRoot.Update` keep running in full. Their remaining
per-frame work at dt 0 — target proxy pushes, `TickWander(0)`, velocity writes
with no physics step, dead-mob proxy cleanup — is either a no-op or harmless
state that was already true. Do **not** early-return the whole `Update` and do
**not** disable the components (D3, I3): `OnDisable` on both roots tears down
combat target proxies.

## Acceptance Criteria

- With `Time.timeScale = 0`, a `SkillDriver` whose slot is ready and whose
  `fireHeld` is true produces zero spawns across many frames.
- A queued loadout edit submitted while `Time.timeScale = 0` still resolves and
  still raises `EditResolved`.
- Cooldown progress is unchanged at `timeScale = 1` — no slot fires later or
  sooner than before this change.
- `ContinuousStreamBehaviour.Runtime.Tick(sink, 0f)` spawns nothing even with a
  pre-saturated accumulator. Cover this with an EditMode test; the runtime is a
  plain class behind `ISpawnSink` and needs no scene.
- `SkillSlotStateEditModeTests` still passes untouched.

## Dependencies

None. Can land before 001.

## Scope Estimate

Small. Three guards, one EditMode test file.

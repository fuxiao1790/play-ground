# 001 — Frame clock singleton + stamp system

## Goal
Provide the two timing signals the trimmer's "both gates" need:
- `FrameStartTime` — wall-clock timestamp captured as early as possible each frame,
  so the trimmer can measure "elapsed since frame start" at end of simulation.
- `SmoothedFrameMs` — EMA of full-frame wall-clock duration, so the trimmer can tell
  a sustained-load frame from a calm one.

## Changes
Add a component + a stamp system (new file, e.g.
`Assets/Scripts/System/Common/CombatFrameClock.cs`):

```csharp
public struct CombatFrameClock : IComponentData
{
    public double FrameStartTime;   // realtimeSinceStartupAsDouble at frame start
    public float  SmoothedFrameMs;  // EMA of previous-frame durations
}
```

Stamp system (managed `SystemBase`; wall-clock read is not Burst-compatible):
- `[UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]` so it runs
  before any simulation work each frame.
- `OnCreate`: create the singleton with `FrameStartTime = now`,
  `SmoothedFrameMs = 0` (treated as "no data yet" -> see gate note in 003).
- `OnUpdate`:
  - `now = Time.realtimeSinceStartupAsDouble`
  - `lastFrameMs = (float)((now - clock.FrameStartTime) * 1000.0)` — duration of the
    **previous** whole frame (its start to this start).
  - Guard the very first frame / editor pauses: clamp `lastFrameMs` to a sane range
    (e.g. ignore if `> 1000ms` or `<= 0`) so a breakpoint/GC hitch doesn't poison the
    EMA.
  - `SmoothedFrameMs = SmoothedFrameMs <= 0 ? lastFrameMs
                       : lerp(SmoothedFrameMs, lastFrameMs, cfg.EmaAlpha)`
  - `FrameStartTime = now`
  - EMA alpha comes from `CombatPoolCleanupConfig` (002); if unavailable, fall back to
    a local const (0.1f).

## Notes
- Read the config singleton via `SystemAPI.TryGetSingleton` so ordering between this
  system's `OnUpdate` and the config's creation is not load-bearing.
- Single writer (this system), read-only elsewhere. No jobs touch it, so no
  dependency plumbing needed.

## Acceptance criteria
- Singleton exists once combat world is running.
- `SmoothedFrameMs` tracks frame time (rises under load, decays when calm) and is not
  corrupted by the first frame or a multi-second editor stall.
- `FrameStartTime` advances once per frame, early.

## Dependencies
- Soft dependency on 002 for `EmaAlpha` (falls back to a const if absent).

## Scope
Small — one component, one ~40-line system.

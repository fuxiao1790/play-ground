# 002 — Cleanup config singleton

## Goal
Hold all trimmer tunables in one place with sane constant defaults, so behavior is
adjustable without hunting through the system and so tests can inject aggressive
values.

## Changes
Add a component (same file as the system, or `CombatFrameClock.cs`):

```csharp
public struct CombatPoolCleanupConfig : IComponentData
{
    public float BudgetMs;            // headroom threshold for BOTH gates
    public float EmaAlpha;            // frame-time EMA smoothing (001)
    public int   RetentionTarget;     // per-batch disabled slots always kept
    public float PoolRatioMultiplier; // trim only if disabled > active * this
    public int   PerBatchDeleteCap;   // max deletes per batch per frame
    public int   MaxDeletesPerFrame;  // global cap across all batches per frame
    public float SliceMs;             // optional wall-clock slice for the delete pass

    public static CombatPoolCleanupConfig Default => new()
    {
        BudgetMs            = 12f,   // idle frames ~5-6ms; 60fps target 16.67ms
        EmaAlpha            = 0.1f,  // ~10-frame smoothing
        RetentionTarget     = 256,   // instant reuse headroom per visual type
        PoolRatioMultiplier = 4f,    // only trim when pool >> active
        PerBatchDeleteCap   = 64,
        MaxDeletesPerFrame  = 256,
        SliceMs             = 0.5f,
    };
}
```

Creation: the trimmer's `OnCreate` (003) creates this singleton from `Default` if it
does not already exist. This keeps ownership with the trimmer and avoids touching
`CombatRoot`.

## Notes
- Defaults are starting points; expect to tune `BudgetMs`, `RetentionTarget`, and the
  caps against a real capture after 003 lands.
- A later `CombatRoot` inspector override (serialized struct copied into the singleton
  at bind) is possible but out of scope here.

## Acceptance criteria
- Singleton present with `Default` values at runtime.
- 001 and 003 read their tunables from it.

## Dependencies
- None (created by 003).

## Scope
Trivial — one struct + defaults.

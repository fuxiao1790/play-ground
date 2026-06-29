# 001 — Core modifier model

Foundation types. No behavior change yet; nothing consumes these until 002–004.

## New files (under `Assets/Scripts/Skills/Modifiers/`)

### `SkillStat.cs`
```csharp
public enum SkillStat
{
    Damage,
    AreaSize,
    ProjectileSpeed,
    ProjectileLifetime,
    RecoverySpeed,
    PierceCount,   // integer stat; resolved value rounded back to int in BuildRuntime
}
```
A fixed, small set. `count` (MultipleProjectiles) is intentionally **not** here — it
stays a behavior override (decision 3). `PierceCount` **is** here: pierce was
promoted to an additive numeric stat (decision 3 amendment) so it stacks/folds.

### `StatModifierAccumulator.cs`
Holds one entry per `SkillStat`. Backed by fixed-size arrays indexed by
`(int)SkillStat` — no dictionary, no per-compile allocation beyond the accumulator
itself.

Per-stat fields: `added`, `increased`, `preMul`, `postMul`
(`preMul`/`postMul` initialised to `1`, others to `0`).

Write API (called only via sinks):
```csharp
void AddAdded(SkillStat stat, float amount);          // added += amount
void AddIncreased(SkillStat stat, float percent);     // increased += percent
void AddMultiplier(SkillStat stat, float mul, MultiplierTiming timing); // pre/post *= mul
```

Read API (called only by the compiler):
```csharp
// effectiveBase = base*preMul + added ;  value = effectiveBase*(1+increased)*postMul
float Resolve(SkillStat stat, float baseValue);
```

`MultiplierTiming { Pre, Post }`.

### Kind sink structs (the "impossible to misuse" boundary)
Each is a readonly struct wrapping an accumulator reference and exposing **only its
kind's** write. A concrete support receives a sink and cannot reach the other kinds
or the accumulator.

```csharp
public readonly struct AddedSink      { void Add(SkillStat stat, float amount); }
public readonly struct IncreasedSink  { void Add(SkillStat stat, float percent); }
public readonly struct MultiplierSink { void Add(SkillStat stat, float mul, MultiplierTiming timing = MultiplierTiming.Post); }
```
(Each forwards to the matching `StatModifierAccumulator` method.)

### Behavior context structs
Shape-typed wrappers over a `SkillDefinition` copy that expose **only behavior
fields**, never numeric base fields (`damage`, `baseAreaSize`, `speed`, `lifetime`
are absent by construction).

```csharp
public readonly struct ProjectileBehaviorContext
{
    // wraps ProjectileDefinition
    public int  Count                { set; }
    public float SpreadDegrees       { set; }
    public float JitterDegrees       { set; }
    public float RepeatHitCooldown   { set; }
    public bool DirectDamageEnabled  { set; }
    public void EnableTracking(float turnSpeedDegrees, float queryIntervalSeconds);
    // EnableTracking sets trackingEnabled=true + the two fields; leaves
    // trackingInitialDelaySeconds at its authored value so BuildRuntime's
    // GetTrackingConfig() preserves it.
}
// NOTE: no PierceCount setter — pierce is a folded numeric stat (SkillStat.PierceCount),
// not a behavior override. Repeat-hit cooldown stays here (it gates repeat hits, not a stat).

public readonly struct AoeBehaviorContext
{
    // wraps AoeDefinitionBase
    public int  Count                { set; }
    public bool DirectDamageEnabled  { set; }
}
```

## Acceptance criteria
- Project compiles with the new types present and unused.
- `StatModifierAccumulator.Resolve` returns `baseValue` for an untouched stat
  (`preMul=postMul=1`, `added=increased=0`).
- Sinks expose only their kind's write; behavior contexts expose no numeric base
  field setter.

## Scope: small. Pure additions, no call sites yet.

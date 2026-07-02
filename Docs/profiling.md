# Profiling CSV Guide

Use the `.csv` export converted from Unity profiler captures to analyze
performance without loading huge files into context.

Never read an entire profiler CSV into context. It can be tens of thousands of
lines.

## CSV Schema

| Column | Example | Meaning |
|---|---|---|
| `Frame` | `0` | Frame index, zero based |
| `FunctionPath` | `Main Thread/PlayerLoop/UpdateScene/...` | Full call-stack path, `/` separated |
| `Function` | `ImpactAoeSpawnApplySystem` | Leaf function name |
| `Total%` | `16.5%` | Percent of frame time including children |
| `Self%` | `2.2%` | Percent of frame time for this node only |
| `Calls` | `1` | Call count that frame |
| `GCAlloc` | `104.3 KB` | Managed GC allocations |
| `TimeMs` | `4.97` | Total time in ms, inclusive |
| `SelfMs` | `0.67` | Self time in ms, exclusive |

`FunctionPath` encodes the full hierarchy. Split on `/` to reconstruct the call
tree.

## Querying With PowerShell

Find the most expensive systems in a single frame:

```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Frame -eq '0' } |
  Sort-Object { [float]$_.TimeMs } -Descending |
  Select-Object -First 20 Function, TimeMs, SelfMs, 'Total%'
```

Find GC-allocating functions across all frames:

```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.GCAlloc -ne '0 B' } |
  Sort-Object { [float]($_.GCAlloc -replace '[^\d.]') } -Descending |
  Select-Object -First 30 Frame, Function, GCAlloc, TimeMs
```

Summarize a system's cost across all frames:

```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -like '*ImpactAoeSpawnApplySystem*' -or $_.Function -like '*LingeringAoeSpawnApplySystem*' } |
  Measure-Object -Property TimeMs -Average -Maximum -Sum |
  Format-List
```

Per-frame total main-thread time:

```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -eq 'PlayerLoop' } |
  Select-Object Frame, TimeMs |
  Sort-Object { [int]$_.Frame }
```

Find hottest Burst jobs by self time:

```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -like '*(Burst)*' } |
  Group-Object Function |
  ForEach-Object {
    [PSCustomObject]@{
      Function = $_.Name
      AvgSelfMs = ($_.Group | Measure-Object SelfMs -Average).Average
      MaxSelfMs = ($_.Group | Measure-Object SelfMs -Maximum).Maximum
      Frames = $_.Count
    }
  } |
  Sort-Object AvgSelfMs -Descending |
  Select-Object -First 20
```

Isolate a subtree by path prefix:

```powershell
$prefix = 'Main Thread/PlayerLoop/UpdateScene/SimulationSystemGroup'
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.FunctionPath -like "$prefix*" } |
  Sort-Object { [float]$_.SelfMs } -Descending |
  Select-Object -First 20 Function, SelfMs, TimeMs, 'Total%'
```

## Spawn Apply Counters

Projectile and AOE apply use one parallel worker-index reuse job per domain, then
cold-create overflow command indices through ECB. Read these counters together:

- `ProjectileSpawnApplySystem.Reuse`
- `ProjectileSpawnApplySystem.Cold`
- `ImpactAoeSpawnApplySystem.Reuse`
- `ImpactAoeSpawnApplySystem.Cold`
- `LingeringAoeSpawnApplySystem.Reuse`
- `LingeringAoeSpawnApplySystem.Cold`

For each domain, reuse plus cold should equal the command count for that apply
tick. Non-zero cold count under uneven worker/chunk distribution is expected.
Repeated stress frames should converge toward more reuse after the resident pool
grows. If cold stays high, check whether worker lanes are poorly matched to
disabled-slot distribution; the documented future lever is a per-chunk
free-slot popcount before lane assignment.

## Grep Approach

Prefer searching over reading full captures.

Examples:

- find rows mentioning a system: `ProjectileSpawnApplySystem`,
  `ImpactAoeSpawnApplySystem`, or `LingeringAoeSpawnApplySystem`
- find GC allocations: `[0-9]+ [KMG]B`
- find Burst jobs: `\(Burst\)`

Always set a result limit to avoid flooding context, for example 50 to 100
lines.

## Reading Tips

- `Total%` is relative to the frame, so 100% means full frame budget.
- `Self%` and `SelfMs` are what to optimize; children have their own rows.
- `FunctionPath` depth indicates nesting; count `/` separators for call depth.
- Burst jobs show as `SystemName:JobName (Burst)`; `SelfMs` is pure compute.
- `JobHandle.Complete` rows signal main-thread stalls waiting on worker jobs.
- For GC pressure, filter `GCAlloc != "0 B"` and inspect `FunctionPath` to find
  the managed callsite.
- Frame 0 is often a warm-up spike; focus on frames 5 and later for steady
  state.

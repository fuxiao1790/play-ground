# Profiling CSV Guide

Use the `.csv` export (converted from Unity's `.data` capture) to analyse performance without loading it into context.
**Never read the entire file into context** — it can be tens of thousands of lines.

## CSV Schema

| Column | Example | Meaning |
|---|---|---|
| `Frame` | `0` | Frame index (0-based) |
| `FunctionPath` | `Main Thread/PlayerLoop/UpdateScene/…` | Full call-stack path, `/`-separated |
| `Function` | `AoeSpawnSystem` | Leaf function name |
| `Total%` | `16.5%` | % of frame time including children |
| `Self%` | `2.2%` | % of frame time for this node only |
| `Calls` | `1` | Call count that frame |
| `GCAlloc` | `104.3 KB` | Managed GC allocations |
| `TimeMs` | `4.97` | Total time in ms (inclusive) |
| `SelfMs` | `0.67` | Self time in ms (exclusive) |

`FunctionPath` encodes the full hierarchy — split on `/` to reconstruct the call tree.

---

## Querying with PowerShell

### Find the most expensive systems in a single frame
```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Frame -eq '0' } |
  Sort-Object { [float]$_.TimeMs } -Descending |
  Select-Object -First 20 Function, TimeMs, SelfMs, 'Total%'
```

### Find GC-allocating functions across all frames
```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.GCAlloc -ne '0 B' } |
  Sort-Object { [float]($_.GCAlloc -replace '[^\d.]') } -Descending |
  Select-Object -First 30 Frame, Function, GCAlloc, TimeMs
```

### Summarise a system's cost across all frames
```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -like '*AoeSpawnSystem*' } |
  Measure-Object -Property TimeMs -Average -Maximum -Sum |
  Format-List
```

### Per-frame total main-thread time (top-level PlayerLoop)
```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -eq 'PlayerLoop' } |
  Select-Object Frame, TimeMs |
  Sort-Object { [int]$_.Frame }
```

### Find hottest Burst jobs (Self time only)
```powershell
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.Function -like '*(Burst)*' } |
  Group-Object Function |
  ForEach-Object {
    [PSCustomObject]@{
      Function   = $_.Name
      AvgSelfMs  = ($_.Group | Measure-Object SelfMs -Average).Average
      MaxSelfMs  = ($_.Group | Measure-Object SelfMs -Maximum).Maximum
      Frames     = $_.Count
    }
  } |
  Sort-Object AvgSelfMs -Descending |
  Select-Object -First 20
```

### Isolate a subtree by path prefix
```powershell
$prefix = 'Main Thread/PlayerLoop/UpdateScene/SimulationSystemGroup'
Import-Csv ProfilerCaptures\play-ground_2026-06-10_10-09-36.csv |
  Where-Object { $_.FunctionPath -like "$prefix*" } |
  Sort-Object { [float]$_.SelfMs } -Descending |
  Select-Object -First 20 Function, SelfMs, TimeMs, 'Total%'
```

---

## Grep Approach (for Claude Code)

Prefer the **Grep tool** over reading the file. Example queries:

- Find all rows mentioning a system: pattern `AoeSpawnSystem`, file `ProfilerCaptures/*.csv`
- Find GC allocations: pattern `[0-9]+ [KMG]B`, output_mode `content`
- Find Burst jobs: pattern `\(Burst\)`

Always set `head_limit` to avoid flooding context (e.g. 50–100 lines).

---

## Reading Tips

- `Total%` is relative to the **frame**, so 100% = full frame budget.
- `Self%` / `SelfMs` is what to optimise — children are their own rows.
- `FunctionPath` depth indicates nesting; count `/` separators for call depth.
- Burst jobs show as `SystemName:JobName (Burst)` — `SelfMs` is pure compute.
- `JobHandle.Complete` rows signal main-thread stalls waiting on worker jobs.
- GC pressure: filter `GCAlloc != "0 B"` then look at `FunctionPath` to find the managed callsite.
- Frame 0 is often a warm-up spike; focus analysis on frames 5+ for steady-state.

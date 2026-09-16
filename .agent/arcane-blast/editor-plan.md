# ArcaneBlast editor plan

Build inside existing `Assets/Vfx/Aoe/Impact/ArcaneBlast.vfx`. Preserve `ImpactCircle` blackboard contract.

## Blackboard and persistent data

Keep exposed: `Positions` (`GraphicsBuffer`), `AreaSizes` (`GraphicsBuffer`), `SpawnCount` (`int`). Keep event `OnSpawn`. Do not expose new graphics buffers or textures.

Add/keep custom float `BaseSize` on root shockwave particles. Preserve current base calculation:

```text
NormalizedBaseSize = AreaSizes[clamp(spawnIndex, 0, SpawnCount - 1)] × 4.5
```

`4.5` is shared one-unit-AOE visual normalization. Ring curves and spark size/speed multipliers operate after this calculation only.

## 1–5. Root shockwave system

1. `Event`: event name `OnSpawn`.
2. `Spawn`: `Single Burst`, `Count = SpawnCount`.
3. `Initialize Particle`, blocks in exact stack order:
   1. `|Set|_Lifetime`: `0.34`.
   2. `|Set|_Position`: `Sample Graphics Buffer(Positions, Clamp(Get|_Spawn Index|Current, 0, SpawnCount - 1))`, converted to `Vector3(x, y, 0)`.
   3. `|Set|_Base Size`: `Sample Graphics Buffer(AreaSizes, same clamped index) × 4.5`.
   4. `|Set|_Size`: `Get|_Base Size|Current`; this makes GPU-event source size normalized before root size-over-life begins.
   5. `|Set|_Angle.Z|Random Uniform`: 0–360 degrees. Ring stays visually circular; spark cluster varies per impact.
   6. `|Set|_Tex Index|Random Uniform`: 0–3. Selects one of four spark-cluster atlas cells.
4. `Update Particle`, blocks:
   1. `|Set|_Size`: `Get|_Base Size|Current × shockwave-size-over-life` (0.15 at t=0, 0.62 at 0.10, 1.16 at 0.34).
   2. `|Set|_Color|Over Life`: white-blue at 0, cyan at 0.12, cobalt at 0.70, transparent at 1.00.
   3. `|Set|_Alpha|Over Life`: 0 at 0, 1 at 0.06, 0.85 at 0.45, 0 at 1.
5. First `Output Particle|Unlit|Quad`: `Orient: Face Camera Position`; assign `arcane-shockwave-ring.png`; alpha blend, alpha clipping 0.5, sort priority 1000, no soft particles.
6. Add second `Output Particle|Unlit|Quad` from same root Update. `Orient: Face Camera Position`; configure 2×2 flipbook layout; assign `arcane-spark-clusters.png`; use particle `texIndex` so root Initialize's 0–3 random value selects a cluster. Alpha blend, alpha clipping 0.5, sort priority 1001, no soft particles.

Spark clusters are painted with several shards near centre and farther shards near rim. The shared root size-over-life expansion moves their painted radial offsets outward as one quad. This is visual expansion, not twelve independently simulated particles.

## Guardrails

- Request buffers and `SpawnCount` have no connections after root Initialize.
- Do not add a GPU Event, second Spawn system, or per-spark particles.
- Keep spark-cluster opacity lower than ring after first 0.08 s. Reduce overlay alpha before reducing ring contrast in dense scenes.

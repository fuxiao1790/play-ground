# ArcaneBlast redesign

## Request

Replace `Assets/Vfx/Aoe/Impact/ArcaneBlast.vfx` single expanding `arcane-explosion.png` quad with arcane shockwave plus sparks flying outward.

## Grounded current state

AgentVFX confirms `VfxDataShape.ImpactCircle`: `Positions` (`GraphicsBuffer`), `AreaSizes` (`GraphicsBuffer`), `SpawnCount` (`int`), event `OnSpawn`. Existing graph calculates `BaseSize = AreaSizes[index] × 4.5`, then expands/fades one textured quad.

## Fidelity defaults

| Axis | Decision |
| --- | --- |
| Anchor/silhouette | Crisp blue-arcane impact: thin expanding ring, sparse star shards. |
| Motion | Ring and radial spark cluster expand for 0.34 s. Spark texture creates outward-motion read; no individual spark simulation. |
| Colour/material | White-blue → cyan → cobalt. Unlit alpha-clipped pixel sprites; no smoke/blur. |
| Budget/delta | 1 particle/request, 2 quad outputs. Replace filled expanding explosion. |

## Runtime and graph contract

- Shape/slot: `VfxDataShape.ImpactCircle`, ArcaneBlast impact.
- Required exposed properties only: `Positions` (`GraphicsBuffer`), `AreaSizes` (`GraphicsBuffer`), `SpawnCount` (`int`). Required event: `OnSpawn`.
- One root system runs `Single Burst(SpawnCount)` and copies its request payload into persistent attributes.
- Root Update feeds two outputs: shockwave ring and radial multi-spark sprite. Spark texture has several atlas cells; `Tex Index` randomization selects one per impact. Its scaled radial placement makes sparks appear to fly outward without simulated child particles.
- `4.5` is existing one-unit-AOE normalization. Preserve it before all artistic multipliers. All graphs using same base calculation render identical base size for `AreaSize = 1`.
- Request buffers, `spawnIndex`, and `SpawnCount` exist only in root Initialize. Both outputs use persistent particle attributes only.
- No gameplay decisions in graph.

## Integration

No C#, ECS, prefab, asset-path, registration, or exposed-property change. Import textures as real-alpha PNGs, then assign them to output slots.

## Manual validation

- Registration accepts `ImpactCircle`: exactly three required exposed properties; no extra `GraphicsBuffer`; `OnSpawn` exists.
- One request: one normalized-size ring plus one multi-spark radial overlay; selected texture variant and random rotation vary each impact.
- `AreaSize = 1` matches existing 4.5-normalized visual base; other area multipliers scale from same base.
- Mixed-value regression: one asset/instance. Spawn A; while alive dispatch B (`A != B`); alternate A/B, vary batch count and request order. Existing rings/sparks retain birth scale, position, and velocity.
- Combat zoom/dense projectile scene: ring remains readable; sparks stay dimmer than ring/hazards/targets.

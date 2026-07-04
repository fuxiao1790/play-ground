# Memory Notes

This folder tracks project memory investigations and decisions that should stay
visible after the immediate implementation work lands.

## Entries

- [Combat pool cleanup](./project-pool-cleanup.md)

## Render Indirect Record

Combat indirect rendering now uploads a compact 2D instance record:
`CombatRenderComponent` is 32 B (`float4 Rotation`, `float3 Position`,
`int RenderMeta`) and the CPU-only base scale/sin/cos state lives in
`CombatRenderAuthoring` (16 B). The upload path remains zero-copy from the render
component list; there is no per-frame repack layer.

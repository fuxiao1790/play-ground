# 006 — Author trail strip graphs and preview

Scope: graph assets, editor validation, and preview driver. Complexity: high.
Dependencies: 001, 002, 004, 005.

Work:

1. Through Unity VFX Graph editor, migrate `MagicBoltTrail` and
   `FireArrowTrail` to Particle Strip initialization/output. Inspect existing
   graphs first using AgentVFX bridge; do not edit `.vfx` serialization.
   Keep `PlagueLink` as `LineSegment` for targeted links.
2. Configure graph static strip count `>= definition.MaxStrips`, and
   `ParticlesPerStrip` high enough for peak accepted point rate multiplied by
   `PointLifetimeSeconds`, plus margin. Set graph to simulate while culled.
   Set Lifetime in Initialize from exposed `PointLifetime` only. Configure
   strip bounds and rendering for gameplay zoom. Width comes from copied
   `TrailWidths[spawnIndex]`, not a shared current-batch property.
3. Wire `ResolvedStripIndices[clamped spawnIndex]` into Initialize's dynamic
   `stripIndex` input. `uint.MaxValue` must reject before point reservation.
   Copy position and width into particle attributes in Initialize. Update and
   Output must use persistent attributes/age/lifetime only; never sample
   request buffers or reuse `spawnIndex` downstream.
4. Validate one shared graph against mixed keys, positions, widths, batch
   counts, ordering, and old tails alive. Verify same-strip continuity,
   near-zero movement, same-frame Begin+End, ring wrap, trail fade, culling,
   and capacity overflow.
5. Extend `CombatVfxPreviewDriver` to preview a
   `ProjectileTrailVfxDefinition` by creating one local
   `ProjectileTrailVfxResources` and driving the same dispatcher/compute path
   with synthetic Begin/Append/End commands. Preview owns and releases its
   local resource. Do not implement a second preview-only slot allocator.

Acceptance:

- Both projectile trail assets satisfy new shape contract and render
  continuous strips; targeted link graph still uses `LineSegment`.
- Graph static capacity matches definition; lifetime is bounded by the one
  authored property.
- Later batches do not move, resize, or join old tails to new trails.
- Preview and runtime use the same key resolver and registration checks.

Validation: manual VFX Graph connection review plus user-run **PlayMode**
`ProjectileTrailVisualIntegrationTests`
(`SharedGraph_MixedWidthAndKeys_StayIndependent`,
`PooledProjectileReuse_DoesNotJoinOldTail`). Export XML under `Logs/`; agent
reviews it. AgentVFX bridge inspection is read-only and does not count as
visual proof.

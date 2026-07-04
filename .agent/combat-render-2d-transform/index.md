# Combat Render — 2D Transform Upload

## Summary

The combat indirect renderer uploads a full `Matrix4x4` (64 B) per instance,
giving `CombatInstanceData`/`CombatRenderComponent` a stride of **68 B**. For a
2D quad whose local `z == 0`, the shader's `mul(M, positionOS)` only ever reads
**7 of the 16 matrix cells**:

```
worldPos.x = m00*x + m01*y + m03      worldPos.z = m23
worldPos.y = m10*x + m11*y + m13
```

The remaining cells are not GPU inputs — `m02/m12` hold the authored base
`VisualRotationSin/Cos`, `m22` holds a max-scale bookkeeping value, and the rest
is identity padding. So the transform sent to the GPU can shrink to a compact 2D
form:

```
float4 rotation   // m00, m01, m10, m11   (2x2 rotation*scale basis)
float3 position   // world x, world y, RenderZ
int    renderMeta // render id (bits 0..30) + align flag (bit 31, CPU-only)
```

**Stride 68 -> 32 B (-53 % per-instance upload)**, no per-vertex trig.

The `float4 + float3 + int` layout has no member crossing a 16-byte boundary
(rotation 0..15, position 16..27, meta 28..31), so it is safe for a tightly
packed HLSL `StructuredBuffer`, matching the existing 68 B struct's proven tight
packing and mirroring `CombatUvBasis`.

## Constraints & Invariants (source)

- **Zero-copy upload is the current design's whole point.** `WriteDirectJob`
  bit-copies `CombatRenderComponent` chunk arrays straight into the upload list
  via `AddRange`; the component is *binary-identical* to the GPU record.
  (`CombatBatchedRenderSystem.cs:106-117`; doc: combat-render-system.md "Key
  Types", render-batch-data.md "Ordering".)
- **The component doubles as CPU scratch across frames.** `ElementFor` reads back
  the authored base `VisualRotationSin/Cos` (stored in `m02/m12`) every frame to
  recombine with the current velocity direction; `VisualScale` is recovered from
  the 2x2 cells. Base rotation/scale is **per-spawn** state (kind base * AOE
  geometry scale) and must persist per entity. (`CombatRenderComponents.cs:42-99,
  339-398`; `CombatRenderPrepareSystem.cs:66-73`.)
- **StructuredBuffer stride must equal the uploaded element size.** You cannot
  upload 68 B elements and read 32 B in the shader; the compact GPU format
  requires the *uploaded* element to be 32 B. (`EnsureInstanceCapacity`,
  `SetData`, shader `_InstanceData`.)
- **Indirect buffers bound via `Material.SetBuffer`, pass tagged
  `LightMode=Universal2D`, `mul(M,v)` untransposed.** Unchanged by this work but
  must not regress. (combat-render-system.md "Critical Constraints";
  [[reference_urp_2d_renderer_lightmode]].)
- **Inactive pooled entities are uploaded too** (query uses
  `IgnoreComponentEnabledState`) and must collapse to a zero-area quad. Today
  `DegenerateMatrix` zeroes the 2x2; the compact form must preserve that
  (rotation = 0 -> all four verts map to `position` -> culled).
  (`CombatBatchedRenderSystem.cs:52-57`, `CombatRenderMatrixUtility.DegenerateMatrix`.)
- **No per-frame structural churn.** Spawn/despawn/reuse only flip enableable
  tags + rewrite value components; any new component must be added to the
  archetype once, not toggled per frame. (combat-render-system.md "Zero
  Structural Churn".) [[project_render_instance_buffers]]

## Shared work (either approach)

The shader + GPU-side is identical regardless of the CPU decision:

- Shader `CombatInstanceData` struct -> `float4 rotation; float3 position; uint
  renderMeta;`, and `vert` reconstructs `worldPos` from the 2x2 basis instead of
  `mul(float4x4, ...)`. UV path (uses mesh `IN.uv`) is untouched.
- `InstanceDataStride` 68 -> 32; `OnCreate` size assert targets the 32 B GPU
  record.
- Docs: combat-render-system.md (Key Types, Data Flow, Perf Notes) +
  render-batch-data.md (stride 68 -> 32).

## Minimal/additive vs. refactor comparison

Both compact the GPU format identically; they differ only in **where the
CPU-scratch vs GPU-wire split lands**.

### Additive — keep `CombatRenderComponent` at 68 B, add a 32 B `CombatInstanceData`

- Resulting data flow: prepare writes the fat 68 B component (unchanged);
  `WriteDirectJob` **repacks** each active component into a compact
  `CombatInstanceData` in the upload list.
- New concepts/types: `CombatInstanceData` (32 B) as a *distinct* GPU type.
- Copies/translations added: one per-instance repack per frame (Burst,
  main-thread `.Run`; extracts 7 floats + meta). Cheap, and writes half the bytes
  it does today.
- Long-term cost: the confusing "base rotation stashed in unused matrix cells"
  hack stays; component (68 B) and wire format (32 B) permanently diverge and
  must be kept in sync by hand (the current `SizeOf==68` guard no longer guards
  the wire size).
- Blast radius: 3 code files (struct, shader, batched system) + docs. **No**
  spawn/command/archetype/test changes. Lowest regression risk.

### Refactor — compact `CombatRenderComponent` to 32 B; move base into a sibling component

- Resulting data flow: `CombatRenderComponent` becomes the 32 B GPU record and is
  still `AddRange`-uploaded **zero-copy**. A new CPU-only
  `CombatRenderAuthoring` (base scale.xy, base sin, base cos; 16 B) holds what
  `m02/m12` used to. Prepare reads authoring + kinematics, writes the compact
  render component (preserving its `RenderMeta`/`RenderZ`).
- Existing types changed/removed: `CombatRenderComponent` layout + all its
  getters/setters; `ElementFor`/`DegenerateMatrix`; spawn commands
  (`ProjectileSpawnCommand.Render`, `AoeSpawnCommand.Render`) carry authoring +
  renderMeta + renderZ instead of a packed matrix; both archetypes gain
  `CombatRenderAuthoring`; ~5 spawn write sites set it; registry
  `GetProjectile/GetAoeRenderComponent` return the pair.
- Copies/translations removed: keeps zero-copy upload (no repack), and deletes
  the matrix-cell storage hack (one source of truth per concept).
- Long-term benefit: ECS memory **68 -> 48 B** per entity; clear split between
  "authored base" and "per-frame GPU record"; wire size guarded again.
- Blast radius: ~10 files incl. spawn commands, both archetypes, 5 write sites,
  registry, prepare, and tests (`SizeOf 68->32`, `objectToWorld` reads). Higher
  regression risk against a system with a documented 8-bug history
  [[project_render_indirect]].

### Decision: **ask user**

Per plan-changes, the additive path trips a structural warning (second type for
the render-record concept + a per-frame translation layer), which normally steers
to the refactor — and the refactor is genuinely cleaner (zero-copy preserved,
less memory, kills the spare-cell hack, one source of truth). **Recommended:
refactor.**

The counter-weight is risk appetite: the refactor touches the carefully-built
spawn/pooling paths and tests, whereas the additive change is contained to 3
files and is near-zero-risk. Because the deciding factor is risk tolerance on a
historically fragile system (not design clarity), this is a user call.

Default rule reminder: two representations of the same domain concept (render
record) should collapse to one source of truth unless there is a concrete
compatibility/migration reason — which here is only diff size / risk.

## Design validation (against invariants)

- Zero-copy: preserved by refactor; **traded** for a cheap repack by additive.
- CPU scratch persistence: refactor moves base to `CombatRenderAuthoring`;
  additive leaves it in the matrix cells (still works).
- Stride == element size: both make the *uploaded* element 32 B. OK.
- Degenerate collapse: both set rotation = 0 for inactive -> zero-area quad. OK.
- Structural churn: refactor adds `CombatRenderAuthoring` to the archetype once
  (value component, never toggled). OK.
- SetBuffer / LightMode / mul untransposed: untouched. OK.

## Tasks — REFACTOR (chosen)

Tasks 001-004 are one compile-coherent change (the struct redefinition breaks
every reference at once); 005 is tests + docs. Land 001-004 together.

- [001](001-data-types.md) — Compact `CombatRenderComponent` to 32 B + add
  `CombatRenderAuthoring`; split accessors.
- [002](002-prepare-transform.md) — Rework `CombatRenderMatrixUtility` +
  `CombatRenderPrepareSystem` to `(kinematics, authoring) -> compact render`.
- [003](003-spawn-plumbing.md) — Registry `Get*` return the pair; command structs
  gain `Authoring`; template builders + fallbacks; both archetypes + all spawn
  write sites set the authoring component.
- [004](004-gpu-submit.md) — Shader struct + vert to 2D; batched system stride
  68->32 + asserts; `WriteDirectJob` stays zero-copy `AddRange`.
- [005](005-tests-docs.md) — Size asserts 68->32, `objectToWorld` test reads,
  `VisualScale`-on-`Render` test builders -> `Authoring`; update both docs.

Key win of the chosen shape: because the component IS the 32 B GPU record again,
`WriteDirectJob` stays a zero-copy `AddRange` (no repack), and the compact form
is produced directly by prepare for both active and degenerate entities.

## Open questions

- Approach decision (additive vs refactor) — asked.
- Confirm the compact 2D form is acceptable at 32 B (vs a tighter 28 B
  angle+scale form that needs per-vertex `sincos`). Recommendation: 32 B 2x2 —
  no per-vertex trig, CPU already has the basis.

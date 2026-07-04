# Combat Render — Static Per-Kind UV Basis Lookup

## Summary
Stop re-uploading the static atlas UV basis (`uvOriginU` + `uvV.xy`, 24 bytes/instance)
every frame for every projectile/AOE. The atlas is **static and contains every sprite
the ECS will ever need**, so every registered kind's UV basis is known after
registration and never changes at runtime. Upload the full per-kind UV table to the GPU
**once** as a small `StructuredBuffer` indexed by `renderId`, and have the shader look it
up by the entity's `renderId`. The per-instance buffer then carries only what actually
changes per frame: the `objectToWorld` matrix + the `renderId`.

Net effect:
- Per-instance GPU upload shrinks **96 → 68 bytes** (~29% less PCIe traffic/frame,
  scales with active count).
- `CombatRenderComponent` shrinks **96 → 68 bytes** → denser ECS chunks → cheaper
  `CombatRenderPrepareSystem` and scatter passes.
- Removes the `float`-punned `RenderTypeId = (int)uvV.z` / `AlignToVelocity = uvV.w`
  hacks in favor of one explicit packed `int`.
- **Zero-copy scatter preserved**: the ECS component stays binary-identical to the GPU
  instance record, so `WriteDirectJob` keeps doing a plain `AddRange`/`Add` with no
  translation struct.

## Rationale for the key decision
The redundant payload is static *per kind*, and the number of kinds is tiny (one per
registered sprite, `_nextRenderId`) versus the number of live instances. The natural
model is: static-per-kind data → one small GPU table uploaded once; per-frame-varying
data → the per-instance buffer. This is the same split the codebase already uses
conceptually (registry owns per-kind resources; the render system owns per-frame
buffers) — we are just moving the UV basis to the side of that line where it belongs.

## Constraints & invariants the change must respect
- **Shader/CS stride must stay in lockstep.** `OnCreate` asserts
  `SizeOf<CombatRenderComponent>() == InstanceDataStride` in
  [CombatBatchedRenderSystem.cs:35](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L35)
  and `== 96` in
  [CombatRenderPrepareSystem.cs:21](../../Assets/Scripts/System/Common/CombatRenderPrepareSystem.cs#L21).
  Both, plus the HLSL `CombatInstanceData` struct
  ([CombatAtlasIndirectSprite.shader:37](../../Assets/Shaders/CombatAtlasIndirectSprite.shader#L37)),
  must move to the new stride together.
- **StructuredBuffers for indirect draws must be bound with `Material.SetBuffer`, not a
  MaterialPropertyBlock** (documented hard-won constraint, combat-render-system.md
  "Critical constraints"). The new `_UvBasis` buffer must be bound the same way, next to
  the existing `_InstanceData` bind at
  [CombatBatchedRenderSystem.cs:152](../../Assets/Scripts/System/Common/CombatBatchedRenderSystem.cs#L152).
- **Matrix is `mul(M, v)`, column-major, untransposed.** Unchanged — we do not touch the
  matrix path.
- **UV basis must reproduce atlas packing rotation/flip.** The affine basis math
  (`origin + u*U + v*V`) is unchanged; only its storage location moves from per-instance
  to per-kind. `ComputeUvBasis`
  ([CombatRenderComponents.cs:286](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L286))
  is untouched.
- **`renderId` domain.** `_nextRenderId` starts at 1;
  `renderId == 0` means non-renderable. The UV table must include index 0 (unused slot)
  so a `renderId == 0` instance never reads out of bounds. Non-renderable entities are
  degenerate-matrixed to zero area and won't rasterize regardless.
- **GPU-buffer lifetime references no ECS memory** and is owned/disposed by its owner
  (registry or render system), mirroring the existing `_instanceBuffer`/`_argsBuffer`
  ownership. Rebuildable if kinds (un)register.
- **Align-to-velocity is CPU-only.** Verified: `ElementFor`
  ([CombatRenderComponents.cs:326](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L326))
  reads it; the shader never does. It must stay resident in the component but need not be
  a distinct GPU field.

## Mechanisms reused vs. introduced
- **Reused:** the registry's existing per-kind `Entries` table
  ([CombatRenderComponents.cs:139](../../Assets/Scripts/System/Common/CombatRenderComponents.cs#L139))
  as the source of truth for UV bases; the `Material.SetBuffer` indirect-bind pattern;
  the `GraphicsBuffer` grow/dispose ownership pattern already used for
  `_instanceBuffer`; the zero-copy "component *is* the instance record" design.
- **Introduced:** one persistent `GraphicsBuffer _uvBasisBuffer`
  (`StructuredBuffer<float4x4>`-adjacent; actually `2 × float4` per kind = 32 B) rebuilt
  from `Entries` when the registry is dirty. This is the only new GPU object; it removes
  more than it adds (24 B/instance/frame + 24 B/component).

## Design validation (against each invariant)
- Stride lockstep: handled by moving all three constants together in one task and
  keeping the `SizeOf == stride` asserts (updated to 68).
- SetBuffer bind: `_UvBasis` bound in `Submit` alongside `_InstanceData`.
- renderId 0 safety: table sized `_nextRenderId` (indices `0.._nextRenderId-1`); slot 0
  written as zero basis.
- Rebuild correctness: a `_uvDirty` flag set on `Register`/`Unregister`; buffer rebuilt
  lazily at the top of the render `OnUpdate` before `Submit`. Covers both "all kinds
  registered at init" (static atlas, the expected case) and any late registration.
- Zero-copy: component and GPU record both become `matrix(64) + uint packedId(4) = 68`;
  scatter stays a straight copy.

## Minimal/additive vs. refactor comparison
- **Minimal/additive** (add `_UvBasis` buffer, keep component at 96, keep uploading UV):
  - resulting data flow: UV basis lives in *two* places (per-kind buffer **and** per
    instance), both uploaded.
  - new concepts/types: the UV buffer, plus a now-dead 24 B in every instance.
  - copies/translations added: none removed; per-frame UV upload still happens.
  - long-term cost: **negative** — pays the complexity of a second UV path for **zero**
    bandwidth win. Pointless.
- **Refactor** (drop UV fields from the component; single per-kind buffer; shrink stride):
  - resulting data flow: UV basis lives in exactly one place (per-kind buffer, uploaded
    once); instance carries only matrix + renderId.
  - existing types changed: `CombatRenderComponent` loses `uvOriginU`/`uvV`, gains one
    packed `int`; shader `CombatInstanceData` loses the two `float4`s, gains a `uint`;
    `Get{Projectile,Aoe}RenderComponent` stop copying UV.
  - copies/translations removed: 24 B/instance/frame upload; the `(int)uvV.z` /
    `uvV.w` float-punning.
  - long-term benefit: one source of truth for UV, smaller component, smaller upload,
    clearer code.
- **Decision: refactor.** The additive path yields no benefit and duplicates the UV
  concept; the refactor collapses a data path and removes float-punning. Matches the
  default rule (two representations of one concept → converge to one source of truth).

## Recommended encoding (packed id) — with fallback
Replace `uvOriginU` + `uvV` with a single `int RenderMeta` on the component:
- bits 0..30 = `renderId` (ample; `_nextRenderId` is tiny)
- bit 31 = align-to-velocity flag
CPU: `RenderTypeId = RenderMeta & 0x7FFFFFFF`, `AlignToVelocity = (RenderMeta >> 31) & 1`.
GPU: `uint id = asuint(inst.renderMeta) & 0x7FFFFFFFu; basis = _UvBasis[id];` (align bit
ignored). Keeps the component at **68 B** and preserves zero-copy.

Fallback if bit-packing is judged too clever: two `int`s (`RenderId`, `AlignToVelocity`)
→ component/stride **72 B**, GPU ignores the align field. Slightly larger, no punning.
Recommend the packed form; it is the smaller, single-field option and the align bit
never reaches the GPU anyway.

## Task list
- [001-uv-basis-gpu-buffer.md](001-uv-basis-gpu-buffer.md) — registry builds/owns/disposes
  the per-kind `_uvBasisBuffer`, dirty-rebuild, exposes it for binding.
- [002-shrink-render-component.md](002-shrink-render-component.md) — drop `uvOriginU`/`uvV`,
  add packed `RenderMeta`; update properties, `SetVisualTransform`, `ElementFor`,
  `DegenerateMatrix`, and `Get{Projectile,Aoe}RenderComponent`; update stride asserts.
- [003-shader-and-bind.md](003-shader-and-bind.md) — shader `CombatInstanceData` → matrix +
  `uint`; add `_UvBasis` lookup; render system `InstanceDataStride 96→68` and bind
  `_UvBasis` in `Submit`; rebuild buffer in `OnUpdate`.
- [004-docs-and-tests.md](004-docs-and-tests.md) — update combat-render-system.md,
  render-batch-data contract, and any test asserting the old stride / setting UV fields.

## Open questions / dependencies
- Task order: 001 → 002 → 003 must land together (stride lockstep); 004 follows. Do not
  merge partially — an intermediate state with mismatched stride will render garbage.
- Confirmed non-issue: no CPU code outside the registry/shader reads `uvOriginU`/`uvV.xy`
  (only `ElementFor`/`DegenerateMatrix`, which read matrix + align, and tests that read
  `objectToWorld`). Grep verified.
- Late registration after first draw is supported via the dirty flag but is not the
  expected path (atlas is static; kinds register during skill setup).

## Cost/benefit honesty
combat-render-system.md's performance notes state the render upload is **not** the
current frame-cost bottleneck (that is `CompleteDependency()` syncing the idle pool).
Treat this as a bandwidth + cache-density + code-cleanliness win that scales with active
instance count and removes float-punning — not an urgent perf fix. It is low-risk and
self-contained.

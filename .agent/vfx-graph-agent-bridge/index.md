---
name: vfx-graph-agent-bridge
description: Agent-facing bridge letting an AI agent read/create/modify/compile VFX Graphs through Unity's own internal editor model, never by hand-editing `.vfx` serialization.
---

# VFX Graph Agent Bridge

## Summary

Build the architecture from `unity_vfx_graph_agent_setup.md`: a thin internal-access
shim that compiles into `Unity.VisualEffectGraph.Editor` (via `.asmref`) to reach
Unity's `internal` VFX Graph model classes, wrapped by a separate, DTO-only public
assembly that will be exposed to the agent through Unity CLI / `com.unity.pipeline`
`[CliCommand]`s. Unity remains the only writer of `.vfx` files — the agent only ever
calls C# methods that mutate the live in-memory graph model, then asks Unity to
compile/save it.

## Rationale

Verified against the actually-installed package source
(`Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/`, package
17.4.0, Unity 6000.4.8f1 — see prior verification pass in conversation) that every
primitive the guide's target API needs exists and is reachable:

- `VFXGraph`, `VFXModel`, `VFXSlot`, `VFXOperator`, `VFXBlock`, `VFXContext` are all
  declared with no access modifier (default `internal`) — confirmed explicit
  `internal class VFXContext` at
  [VFXContext.cs:59](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Models/Contexts/VFXContext.cs#L59).
  This is exactly why the `.asmref` merge trick is required and is the correct
  mechanism, not a workaround for something otherwise reachable.
- `Unity.VisualEffectGraph.Editor.asmdef`
  ([asmdef](../../Library/PackageCache/com.unity.visualeffectgraph@1d000c792c1e/Editor/Unity.VisualEffectGraph.Editor.asmdef))
  has no `overrideReferences`/precompiled restriction blocking a folder-level
  `.asmref` merge.
- Node/slot/graph primitives are public members of these internal types, so once our
  code is compiled into the same assembly they are fully callable. Exact mapping
  recorded per-task below.

## Constraints & Invariants

- **Assembly boundary**: only code under `Assets/AgentVFX/InternalAccess/` may
  reference `UnityEditor.VFX` types. Everything else talks to
  `AgentVfxInternalBridge` through plain DTOs. Source: guide §8, §14 — enforced
  structurally by putting all internal-facing files in one folder and giving the
  rest of the project no reference to `Unity.VisualEffectGraph.Editor` internals.
- **Editor-only, main-thread**: all bridge calls run on the Unity main thread inside
  the Editor; no threading/Burst concerns apply (this is editor tooling, not the
  combat ECS runtime — `Docs/coding-standards.md`'s ECS/allocation rules do not
  apply here).
- **Unity is the only writer of `.vfx`**: the bridge never touches serialized
  YAML/text; every mutation goes through the live `VFXModel` graph, then
  `AssetDatabase`/`VisualEffectResource` writes it. Source: guide §18.
- **Version-sensitive**: all internal API calls are pinned to package
  `com.unity.visualeffectgraph@17.4.0` / Unity `6000.4.8f1`. Re-verify against
  source after any Unity/VFX Graph upgrade (guide §26, §27).
- **CLI layer is unverified**: `com.unity.pipeline` / Unity CLI are not installed on
  this machine (`Packages/manifest.json` has no such dependency; `unity` is not on
  PATH). The `[CliCommand]` surface (task 006) is written to the guide's documented
  signature only — it has not been checked against real `Unity.Pipeline.Commands`
  source and will not compile until that package is installed. User is installing
  the CLI/Pipeline themselves (per prior discussion) — flagged as an open item, not
  silently assumed to work.
- **Filesystem scope** (guide §25): bridge mutation commands only operate on assets
  under `Assets/VFX/**` and `Assets/AgentGenerated/**`; the bridge must reject paths
  outside these roots rather than silently no-op.
- **Test policy** (`Docs/project-overview.md`): agents write tests but never run the
  Unity test runner. Compatibility tests (task 007) are written for the user to run
  and report results from, per `Docs/testing.md`.

## Mechanisms Reused vs. Introduced

**Reused (all of Unity's existing machinery, per guide §2):** `VFXGraph`,
`VFXModel`/`VFXContext`/`VFXBlock`/`VFXOperator`/`VFXSlot`, `VFXLibrary` type
descriptors, `VisualEffectResource` import/serialization, `AssetDatabase` save,
`VFXGraph.errorManager` diagnostics, Editor `Undo` API.

**Introduced (new, thin):** `AgentVfxInternalBridge` (internal-access shim),
stable bridge-side ID map, agent-facing DTOs, `[CliCommand]` surface. No custom
serializer, compiler, or parallel graph representation — this is additive-only,
there is no existing system to refactor toward.

## Design Validation

| Invariant | How satisfied |
|---|---|
| Only `InternalAccess/` touches `UnityEditor.VFX` | All internal calls confined to `Assets/AgentVFX/InternalAccess/*.cs`; `Assets/AgentVFX/Editor/` only sees DTOs and the bridge's public static API |
| Unity is the only `.vfx` writer | Mutations go through `VFXModel.AddChild/RemoveChild/SetSettingValue`, `VFXSlot.value/Link`, then `AssetDatabase.SaveAssets()` / `VisualEffectResource.WriteAssetWithSubAssets()` — never raw file I/O |
| Stable IDs across read/modify/read | Bridge-side `ConditionalWeakTable<UnityEngine.Object,string>` map, assigned once per object for the life of the Editor session — object identity survives our own edits and saves since Unity does not recreate `VFXModel` sub-assets on save |
| Filesystem scope | Bridge validates every asset path against `Assets/VFX/` and `Assets/AgentGenerated/` prefixes before opening/mutating |

## Minimal/Additive vs. Refactor Comparison

Not applicable in the usual sense — there is no existing VFX authoring system in
this codebase to refactor toward. This is greenfield, additive-only tooling that
sits beside gameplay code with zero shared types or call paths.

- **Decision: additive.**
- **Reason:** no second representation of anything is being created — the bridge
  wraps Unity's own single source of truth (`VFXGraph`) rather than duplicating it.

## Task List

1. [001-internal-access-core.md](001-internal-access-core.md) — `.asmref`, bridge
   skeleton, ID map, `Ping`, graph open/read/save, path-scope guard.
2. [002-type-library-and-node-ops.md](002-type-library-and-node-ops.md) — type
   listing/describe, node create/delete/move/configure.
3. [003-slot-and-blackboard-ops.md](003-slot-and-blackboard-ops.md) — slot
   read/set/connect/disconnect, blackboard read/add/remove.
4. [004-compile-and-errors.md](004-compile-and-errors.md) — compile + structured
   diagnostics.
5. [005-editor-api-and-dtos.md](005-editor-api-and-dtos.md) — `AgentVFX.Editor`
   assembly, DTOs, stable public API wrapping the bridge.
6. [006-cli-commands.md](006-cli-commands.md) — `[CliCommand]` surface (unverified
   against real `Unity.Pipeline` source — flagged, not silently trusted).
7. [007-preview-and-tests.md](007-preview-and-tests.md) — screenshot/preview loop
   (public APIs only) + EditMode compatibility tests (written, not run).

## Open Questions

- Real signature/namespace of `Unity.Pipeline.Commands.CliCommand` is unverified —
  task 006 is written to the guide's example and must be compile-checked once the
  user installs `com.unity.pipeline`.
- Whether `com.unity.pipeline` and the Unity CLI are real, currently-installable
  products is outside what can be checked from this repo — user is handling
  install themselves.

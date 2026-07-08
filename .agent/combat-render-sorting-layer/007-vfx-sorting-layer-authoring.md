# 007 - VFX Sorting Layer Authoring

## Status: revised — per-asset authoring doesn't apply to this codebase

The original version of this subtask assumed VFX prefabs are pre-authored
GameObjects whose Inspector-serialized Renderer sorting fields could be set
once, ahead of time. That's not how VFX is actually created here:
`CombatVfxDispatcher.Register` builds every `VisualEffect` GameObject from
scratch at runtime (`new GameObject(...)` +
`go.AddComponent<VisualEffect>()`, [CombatVfxDispatcher.cs:82-85](../../Assets/Scripts/System/Vfx/CombatVfxDispatcher.cs#L82-L85)),
from a `VisualEffectAsset` that carries no renderer/sorting data of its own.
There is no persistent GameObject to open in the Inspector and author a
Sorting Layer onto — every VFX renderer defaults to `Default`/order `0` and
was observed interleaving with player/mob by Y-position instead of sitting
below `CombatSprites` (reported bug: draw order came out
"ECS sprites → VFX → mobs/player" instead of "VFX → ECS sprites →
mobs/player").

## Scope

`CombatVfxRoot`'s GameObject (the single scene/prefab object all VFX
instances are parented under —
`go.transform.SetParent(parent, false)` in `CombatVfxDispatcher.Register`,
where `parent` is `CombatVfxRoot.transform`,
[CombatVfxRoot.cs:15](../../Assets/Scripts/System/Vfx/CombatVfxRoot.cs#L15)).
No VFX asset or prefab needs touching.

## Change (as implemented)

Add a `SortingGroup` component directly to the `CombatVfxRoot` GameObject in
the scene, Sorting Layer `CombatVfx`, Order in Layer `0`. Every VFX
GameObject is a child of that transform and has no `SortingGroup` of its own,
so it inherits sorting from the nearest ancestor group automatically — same
mechanism already used for the combat sprite `MeshRenderer`
(see [[project_combat_render_sorting_group_proxy]] and
[003-persistent-mesh-renderer.md](003-persistent-mesh-renderer.md)'s
"SortingGroup proxy" status update). No code changes; one Inspector step.

## Acceptance Criteria

- Every VFX instance spawned through `CombatVfxDispatcher.Register` renders
  on the `CombatVfx` Sorting Layer, without any per-asset authoring.
- Visual check: with combat sprites and VFX overlapping on screen, VFX draws
  behind (bottom of) combat sprites, confirming `CombatVfx` sorts below
  `CombatSprites`.

## Dependencies

Depends on 001 (the `CombatVfx` layer must exist to assign it) and requires
`CombatVfxRoot`'s scene/prefab instance to be identifiable/editable — no
dependency on 002-006 (this is pure Inspector authoring on an existing
GameObject, no VFX rendering code is touched).

## Complexity

Small — one component, one field, one GameObject. No per-asset long tail
(the original plan's main cost concern no longer applies since nothing is
touched per-asset).

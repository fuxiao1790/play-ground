---
name: 002-collapse-vfx-root-and-dispatch
description: Replace the faction-keyed CombatVfxRoot registry with a single static instance and drain the whole buffer in dispatch
---

# 002 — Collapse `CombatVfxRoot` to one instance; dispatch drains whole buffer

## Goal

Remove faction routing from the managed VFX layer. One `CombatVfxRoot` instance
serves all VFX; the dispatch system drains the staging buffer in one pass with
no per-faction split.

## Changes

### [CombatVfxRoot.cs](../../Assets/Scripts/System/Vfx/CombatVfxRoot.cs)

- Remove `private static readonly CombatVfxRoot[] ByFaction = new CombatVfxRoot[3];`
  and the `private CombatFaction faction;` field.
- Replace with `public static CombatVfxRoot Instance { get; private set; }`.
- `Awake`: after building the dispatcher, set `Instance = this;`.
- `OnDestroy`: `if (Instance == this) Instance = null;` (replacing the
  `ByFaction[(int)faction]` clearing), then dispose the dispatcher as today.
- Delete `BindFaction(CombatFaction)` and `TryGetByFaction(...)`.
- `DrainAndDispatch(NativeArray<VfxSpawnRequestElement>)` stays as-is.
- Drop `using PlayGround.System.Common;` if `CombatFaction` is no longer
  referenced in the file.

Note on multi-instance: if two roots ever exist, last `Awake` wins — acceptable
for a visual singleton (see index "Risks"). This matches the render registry
being a single managed singleton.

### [CombatVfxDispatchSystem.cs](../../Assets/Scripts/System/Vfx/CombatVfxDispatchSystem.cs) — `OnUpdate` drain

- Remove the `playerRequests` / `mobRequests` `NativeList`s and the per-element
  faction-split loop (lines 29-42).
- Remove the two `TryGetByFaction` branches (lines 45-53).
- Replace with a single drain:
  ```csharp
  CombatVfxRoot.Instance?.DrainAndDispatch(buffer.AsNativeArray());
  buffer.Clear();
  ```
  Keep the existing `CompleteDependency()` and the early `return` when
  `buffer.Length == 0`.
- Drop `using Unity.Collections;` if the temp lists were its only use.

> The `buffer` source (scope vs. dedicated VFX entity) and the `scopeQuery`
> declaration are changed in 003. 002 only rewrites the drain body; leave the
> entity-resolution lines for 003.

### [PlayerSkillDriver.cs](../../Assets/Scripts/Skills/PlayerSkillDriver.cs)

- Remove the two `vfxRoot.BindFaction(CombatFaction.Player);` calls
  (lines 41-42 in `Start`, lines 75-76 in `BindCombatRoot`). Keep the
  `vfxRoot.Register(...)` calls unchanged.

### [MobProjectileAttack.cs](../../Assets/Scripts/Mob/MobProjectileAttack.cs)

- No change required (it never calls `BindFaction`). The `vfxRoot.Register(...)`
  calls stay; they are no-ops today because `MobRoot` passes `vfxRoot = null`.

## Acceptance criteria

- `CombatVfxRoot` exposes a single `static Instance`; no faction registry,
  `BindFaction`, or `TryGetByFaction` remain.
- `CombatVfxDispatchSystem.OnUpdate` drains the buffer once into
  `CombatVfxRoot.Instance` and clears it, with no per-faction allocation.
- No caller references `BindFaction` / `TryGetByFaction`.
- Compiles together with 001 and 003.

## Scope

Small-to-medium. One managed class simplified, one system method rewritten, two
call-site removals. Removes two per-frame `Allocator.Temp` lists.

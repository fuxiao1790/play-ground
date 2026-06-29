---
name: 005-update-docs
description: Update VFX reference, contract, flow, and layer docs to drop faction routing and scope coupling
---

# 005 — Update VFX docs

## Goal

Bring the design docs in line with the faction-agnostic, scope-decoupled VFX
system. Docs are "current intent" references and must match code after the change.

## Changes

### [Docs/reference/simulation/vfx-system.md](../../Docs/reference/simulation/vfx-system.md)

- **Data Flow** block: remove `CombatFaction Faction` from the `VfxPendingSpawn`
  field list; change the buffer host from "shared `CombatScope`" to the dedicated
  VFX singleton entity; remove "(splits requests by Faction; routes each faction
  to its `CombatVfxRoot`)" — dispatch drains one buffer into the single
  `CombatVfxRoot.Instance`.
- **Key Classes**: `CombatVfxRoot` — replace the "static faction-keyed registry
  (`ByFaction[]`) … `TryGetByFaction` … `BindFaction`" description with a single
  static `Instance` set in `Awake`. `CombatVfxDispatchSystem` — remove "splits
  requests by `Faction` (Player / Mob)" and "resolves each `CombatVfxRoot` via
  `TryGetByFaction`"; describe the single drain. `VfxFlushJob` — "drains into the
  VFX singleton buffer" (not "shared scope buffer").
- Note the resemblance to sprite rendering (faction-agnostic; presentation-owned
  singleton entity), mirroring `CombatBatchedRenderSystem`.

### [Docs/contracts/vfx-requests.md](../../Docs/contracts/vfx-requests.md)

- **Fields / Shape**: remove the `CombatFaction Faction` bullet (line 22).
- **Lifetime**: "flushed to the VFX singleton buffer" instead of "scope buffers"
  (line 47).

### [Docs/flows/vfx-dispatch.md](../../Docs/flows/vfx-dispatch.md)

- Step 2: "append requests to `DynamicBuffer<VfxSpawnRequestElement>` on the VFX
  singleton entity" (not "the shared `CombatScope`") (lines 10-11).
- Step 4: "resolves the single `CombatVfxRoot.Instance`" (not "through the scope
  VFX catalog") (line 13).

### [Docs/layers/presentation-and-feedback.md](../../Docs/layers/presentation-and-feedback.md)

- Line 31: "`VfxSpawnRequestElement` scope buffers." → reference the VFX
  singleton buffer.

## Acceptance criteria

- No VFX doc describes a faction split, `ByFaction`/`BindFaction`/`TryGetByFaction`,
  or the VFX buffer living on `CombatScope`.
- Docs describe the single `CombatVfxRoot.Instance` and the presentation-owned
  VFX singleton entity.

## Scope

Small, docs-only. Land with or right after 001-004.

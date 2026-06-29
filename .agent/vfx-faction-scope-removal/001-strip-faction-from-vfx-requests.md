---
name: 001-strip-faction-from-vfx-requests
description: Remove the Faction field from the two VFX request types, the emitter writes, and the flush-job faction guard
---

# 001 — Strip faction from VFX request data, emitters, and flush

## Goal

Remove `CombatFaction Faction` from the VFX request payloads and everything that
reads or writes it. Faction stays on gameplay events; only the visual-only VFX
requests lose it.

## Changes

### Request types — [VfxEcsComponents.cs](../../Assets/Scripts/System/Vfx/VfxEcsComponents.cs)

- `VfxPendingSpawn`: remove `public CombatFaction Faction;` (line 10).
- `VfxSpawnRequestElement`: remove `public CombatFaction Faction;` (line 20).
- Drop the now-unused `using PlayGround.System.Common;` if nothing else in the
  file needs it (it imports `CombatFaction`; confirm no other use remains).

### Flush jobs — [VfxFlushJob.cs](../../Assets/Scripts/System/Vfx/VfxFlushJob.cs)

- `VfxFlushJob.Execute` (line 20): change the guard
  `if (p.Faction == CombatFaction.None || !VfxBuffers.HasBuffer(Scope))` to
  `if (!VfxBuffers.HasBuffer(Scope))`.
- Remove `Faction = p.Faction,` from the appended `VfxSpawnRequestElement`
  (line 27).
- `VfxStreamFlushJob.Write` (line 61): same guard simplification and same
  `Faction =` removal (line 68).
- Drop `using PlayGround.System.Common;` if no longer referenced.

### Emitter writes (remove `Faction = identity.Faction,` / `Faction = e.Faction,` from the VFX request only)

- [CombatLifetimeSystem.cs:87](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L87)
  — projectile expire `VfxPendingSpawn`.
- [CombatLifetimeSystem.cs:122](../../Assets/Scripts/System/Common/CombatLifetimeSystem.cs#L122)
  — AOE expire `VfxPendingSpawn`.
- [ProjectileCollisionSystem.cs:339](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L339)
  — projectile hit `VfxPendingSpawn`.
- [ProjectileCollisionSystem.cs:378](../../Assets/Scripts/System/Projectile/ProjectileCollisionSystem.cs#L378)
  — `Deactivate` expire `VfxPendingSpawn`.
- [AoeCollisionCore.cs:257](../../Assets/Scripts/System/Aoe/AoeCollisionCore.cs#L257)
  — AOE hit `VfxPendingSpawn`. **Do not** touch the `ProjectileSpawnEvent.Faction`
  (line 227) or `AoeSpawnEvent.Faction` (line 245) writes — those are gameplay.
- [AoePulseVfxSystem.cs:72](../../Assets/Scripts/System/Aoe/AoePulseVfxSystem.cs#L72)
  — pulse `VfxPendingSpawn`.
- [AoeSpawnExpansionSystem.cs:129](../../Assets/Scripts/System/Aoe/AoeSpawnExpansionSystem.cs#L129)
  — spawn (trigger 0) `VfxSpawnRequestElement` written directly. Remove
  `Faction = e.Faction,`; keep using `e.Faction` for the gameplay
  `AoeSpawnCommand`/expansion stamping elsewhere in the system.

## Do NOT change

- `identity.Faction == CombatFaction.None` gameplay early-outs in
  `ProjectileCollisionSystem` (line 186) and `AoeCollisionCore` (line 95).
- Any `AoeSpawnEvent.Faction`, `ProjectileSpawnEvent.Faction`,
  `CombatHitEvent`, `TargetFaction`, or `identity.Faction` reads — all gameplay.

## Acceptance criteria

- `VfxPendingSpawn` and `VfxSpawnRequestElement` no longer contain a `Faction`
  field.
- No VFX request write sets `Faction`.
- Flush jobs append every dequeued/streamed request whose target buffer exists.
- Project compiles (note: `CombatVfxDispatchSystem` still references
  `e.Faction` until 002 lands — 001 and 002 are one compile unit; see index
  "Dependencies / ordering").

## Scope

Small, mechanical. ~10 edits across 6 files. No behavioral change beyond the
faction field removal itself.

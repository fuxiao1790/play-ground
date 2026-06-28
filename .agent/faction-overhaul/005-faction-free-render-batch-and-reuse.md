# 005 — Faction-free render batch id + cross-faction spawn reuse

## Goal
Drop the faction namespacing from `CombatRenderBatchId` (`(faction<<16)|renderId` →
`renderId`) and make the apply systems read faction **per command**, so dead slots are
reused across factions (requirement: "reuse any entity with the same archetype
regardless of faction") and player/mob entities of the same visual share a render batch.

## Background
- `CombatRoot` publishes render resources under `BatchIdFor(faction, renderId)` and the
  shared component is set to the same id
  ([CombatRoot.cs:618-648](../../Assets/Scripts/System/Common/CombatRoot.cs#L618-L648)).
- Apply buckets commands by `((int)cmd.Faction<<16)|cmd.RenderTypeId`, extracts faction
  via `key.BatchId >> 16`, and cold-creates with the same shared id
  ([ProjectileSpawnApplySystem.cs:137,206,325,456](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs#L137),
  [AoeSpawnApplySystem.cs:126,154,303](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs#L126)).
- Per-command faction already exists from expansion stamping (index I2), so it is the
  correct reuse-time source once buckets can mix factions.

With one root the render-id space is globally unique (single `nextRenderId`), so faction
in the id is redundant.

## Changes
1. **`CombatRoot` render registry** (this part of CombatRoot lands here; the rest of the
   root refactor is 006):
   - `BatchIdFor` → return `renderId` (drop `(faction<<16)`), or inline the registry key
     as `renderId`. Registry publish (L642) and teardown removal (L155) key by `renderId`.
2. **Projectile apply** ([ProjectileSpawnApplySystem.cs](../../Assets/Scripts/System/Projectile/ProjectileSpawnApplySystem.cs)):
   - `BucketCommandsJob` key (L325): `new ProjectileSpawnKey(cmd.RenderTypeId)`.
   - Remove the `faction = key.BatchId >> 16` extraction (L137); stop passing a bucket
     `Faction` into the reuse job and `ProjectileSpawnWork`.
   - Reuse jobs (`BasicProjectileSpawnJob`, `ChildSpawnerProjectileSpawnJob`): drop the
     `Faction` field; set `identities[i].Faction = cfg.Faction` and
     `HitPayloadFor(in cfg, cfg.Faction)`.
   - Cold-create (L456, L647): `AddSharedComponent(... { Value = cmd.RenderTypeId })`,
     `RecordCommonProjectileReset(ecb, entity, cmd.Faction, cmd)`.
3. **AOE apply** ([AoeSpawnApplySystem.cs](../../Assets/Scripts/System/Aoe/AoeSpawnApplySystem.cs)):
   - `AoeSpawnKey.BatchId` (L126): `cmd.RenderTypeId`; keep `Lingering`/`HasTimedSpawner`
     dimensions.
   - Remove `faction = key.BatchId >> 16` (L154); drop `Faction` from `AoeSpawnJob` and
     `AoeSpawnWork`.
   - In the job set `identities[i].Faction = cfg.Faction` and
     `HitPayloadFor(cfg.HitPayload, cfg.Faction)`; cold-create (L303) uses
     `{ Value = cmd.RenderTypeId }` and `IdentityFor(cmd.Faction, cmd)` /
     `HitSpawnFor(cmd, cmd.Faction)`.

## Acceptance Criteria
- `CombatRenderBatchId.Value == RenderTypeId` on every spawned projectile/AOE.
- A `Mob` spawn reuses a disabled slot originally spawned by a `Player` of the same
  render id (and vice versa), with the reused entity's `identity.Faction` set to the new
  spawn's faction and the render unchanged.
- Render output unchanged (registry key == batch id == renderId).
- `CombatRenderResourceRegistry` no longer has per-faction-namespaced keys.

## Dependencies
None hard, but conceptually paired with 006 (both touch `CombatRoot`). Verify rendering
end-to-end after 006.

## Scope
Medium. Touches the two apply systems and the `CombatRoot` render-registry keying.

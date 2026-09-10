# 004 — Asset and catalog migration

**Depends on:** [001-onhittrigger-collapse.md](001-onhittrigger-collapse.md)
(the `OnHitTrigger` script must exist before the asset can be created).
**Scope:** Small, but **performed by the user in the Unity Editor.** No `.asset`
or `.meta` file is hand-edited (index I10).

## Why the surface is this small

A GUID sweep over `Assets/**/*.{asset,prefab,unity}` found exactly one file
referencing any trigger asset: `Assets/ScriptableObjects/UI/SkillBar/TriggerCatalog.asset`.
No loadout, prefab or scene holds a trigger reference — player loadouts are
assembled at runtime from the catalog (index I9). So there is no save-data or
scene migration, only the catalog.

Current catalog order and GUIDs:

| Entry | Asset | GUID |
|---|---|---|
| 1 | `OnAoeHitSpawnTrigger.asset` | `8a0e8f4655664c4ca5fd11198d90f001` |
| 2 | `OnImpactAoeTrigger.asset` | `502349cdbd8fc624f8dd28ba7b2eebbb` |
| 3 | `OnImpactProjectileTrigger.asset` | `1a2b3c4d5e6f708192a3b4c5d6e7f801` |
| 4 | `StackTrigger.asset` | `c4000000000000000000000000000001` |
| 5 | `IntervalSpawnTrigger.asset` | `d077c5b467c1bf84a81d840bd2ab870c` |

There is no `OnImpactTargetedTrigger.asset` — that type was never authored as an
asset, so targeted effects are currently unreachable from the catalog. The
unified trigger fixes that for free.

`OnExpireTrigger.asset` exists but is in no catalog and has no code path; leave
it alone (index Scope).

## Steps for the user

1. **Create the asset.** `Assets > Create > PlayGround > Skills > Triggers > On Hit`,
   saved as `Assets/ScriptableObjects/Skills/Triggers/OnHitTrigger.asset`.
2. **Fill in the UI fields** — `displayName`, `description`, `icon`. Reusing the
   icon from `OnImpactAoeTrigger.asset` keeps the bar visually familiar. The
   description should say the effect skill decides what spawns, since that is
   now the only thing that varies.
3. **Set the mana-cost factors.** `OnHitTrigger` has no attributes of its own,
   so the only numbers to carry over are `manaCostMultiplier` and
   `manaCostIncreased`. Check all three old assets and copy whichever values
   were tuned away from `1`.
4. **Edit `TriggerCatalog.asset`.** Remove entries 1-3 and add `OnHitTrigger`
   in their place, so the catalog reads: `OnHitTrigger`, `StackTrigger`,
   `IntervalSpawnTrigger`.
5. **Delete the three old assets** and their `.meta` files through the Editor:
   `OnAoeHitSpawnTrigger.asset`, `OnImpactAoeTrigger.asset`,
   `OnImpactProjectileTrigger.asset`.
6. **Retune the burst on the projectile skill sets, if you want the old feel.**
   `OnImpactProjectileTrigger.asset` shipped with `spawnCount: 4` and
   `spreadDegrees: 360`, so every impact-projectile chain fired four extra
   projectiles in a full circle regardless of the effect set. That is now the
   effect set's own business: raise its `ProjectileDefinition.count` and set
   `spreadDegrees = 360`, or attach a `MultipleProjectilesSupport` (which adds
   to both and prices the mana cost accordingly). Doing nothing is a legitimate
   choice — it just means impact chains fire whatever the effect skill fires
   when cast directly.

## Ordering note

After task 001 lands, Unity will report the three old assets as having a missing
script and the catalog will show three `None` entries until steps 1-5 are done.
That window is benign — a `null` catalog entry is skipped and a null
`TriggerToNext` compiles to no link — but the skill bar will look broken until
the migration is finished, so run 001 and 004 close together.

## Acceptance criteria

1. `OnHitTrigger.asset` exists; its inspector shows only the UI fields and the
   two mana-cost factors — no `spawnCount`, no `spreadDegrees`.
2. `TriggerCatalog.asset` lists exactly three triggers, none of them null.
3. The three old trigger assets and their `.meta` files are gone.
4. `OnExpireTrigger.asset` and `StackTrigger.asset` are untouched.
5. In play, wiring projectile → `OnHitTrigger` → AOE reproduces the old
   `OnImpactAoe` behavior, and AOE → `OnHitTrigger` → projectile now spawns the
   burst on each AOE hit.

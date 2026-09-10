# 003 — Asset and Catalog Migration (Unity Editor)

**Scope:** small, but **user-performed in the Unity editor.** These are asset
edits — do not hand-edit the YAML.
**Depends on:** 001 (the `StackTrigger` fields must exist before they can be
authored).

## Current state

| Asset | Values | Referenced by |
|---|---|---|
| `Supports/StackingSupportLow.asset` | threshold 10, lifetime 5, stacksPerHit 1 | `SupportCatalog`, plus 4 skill sets |
| `Supports/StackingSupportHigh.asset` | threshold 30, lifetime 5, stacksPerHit 1 | `SupportCatalog` only |
| `Triggers/StackTrigger.asset` | (no stack fields yet) | `TriggerCatalog` only |
| `SkillSet/Stacking{ArcaneStorm,MagicBolt,MagicBolt2,Sigil}SkillSet.asset` | carry `StackingSupportLow` | **nothing** — verified orphaned |

The player-facing surface is the three catalogs under
`ScriptableObjects/UI/SkillBar/`; the loadout is assembled from them at runtime,
so the catalogs are what actually needs to stay coherent.

The Low/High presets are **not** migrated — breaking them is accepted. That drops
what would have been the bulk of this task; no new trigger assets are created.

## Steps

1. **Tune `Triggers/StackTrigger.asset` if you want.** It picks up the type
   defaults (threshold 3 / lifetime 4 / stacksPerHit 1) with no action. Set
   different values here if the default feel is wrong; there is no preset to
   match.
2. **Remove `StackingSupportLow` and `StackingSupportHigh` from `SupportCatalog.asset`.**
3. **Clear the support slot on the four stacking skill sets**
   (`StackingArcaneStormSkillSet`, `StackingMagicBoltSkillSet`,
   `StackingMagicBolt2SkillSet`, `StackingSigilSkillSet`). They become ordinary
   skill sets — nothing references them, so this is cosmetic; consider dropping
   the `Stacking` prefix from the names since a set is no longer intrinsically a
   detonation. Deleting them outright is also fine if they were fixtures.
4. **Delete `StackingSupportLow.asset` and `StackingSupportHigh.asset`** (with
   their `.meta` files) once steps 2-3 leave them unreferenced.

## Ordering note

Step 1 must follow 001. If you run the game between 001 and this task, stacking
still works at default tuning.

## Acceptance criteria

- [ ] `SupportCatalog` lists no stacking entries; `TriggerCatalog` still lists
      `StackTrigger`.
- [ ] No asset in `Assets/` references the deleted support GUIDs
      (`98db028a9d87c7b48b8781e058d379fe`, `c3000000000000000000000000000001`).
- [ ] Unity console shows no missing-script warnings on load.
- [ ] In play, picking the stack trigger from the catalog and wiring
      `applicator → stack trigger → detonation set` detonates at the threshold
      authored on the trigger asset.

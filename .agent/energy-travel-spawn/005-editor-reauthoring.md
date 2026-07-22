# 005 — Editor re-authoring (USER STEPS)

These are Unity editor actions performed by the user, not code edits. The field renames
deliberately do not auto-migrate the rate value (old interval seconds is wrong under the new
energy/second meaning), so each affected asset must be re-authored. Per project convention,
authored assets are edited in the editor, never by hand-writing YAML.

## Steps

1. **Child skill assets** (any `ProjectileSkill` / `AoeSkill` / `LingeringAoeSkill` used as a
   travel-spawn child): set `spawnEnergyCost`. Cost is in energy units; cadence from a given
   spawner is `spawnEnergyCost / energyPerSecond` seconds. Leaving the default (`1`) is fine
   for a "one energy per spawn" skill.

2. **Travel trigger assets** — set `energyPerSecond` (the spawner's charge speed) on each:
   - `Assets/ScriptableObjects/Player/Skills/Triggers/ProjectileIntervalSpawnTrigger.asset`
   - `Assets/ScriptableObjects/Player/Skills/Triggers/ProjectileIntervalSpawnTrigger2.asset`
   - `Assets/ScriptableObjects/Player/Skills/Triggers/AoeIntervalSpawnTrigger.asset`
   To reproduce a former interval `T` with cost `C`, set `energyPerSecond = C / T`.
   `energyJitterPercent` carries over from the old `intervalJitterPercent` automatically
   (FormerlySerializedAs) and can be left as-is.

3. **Verify in play** any loadout that used travel spawn
   (e.g. `Assets/ScriptableObjects/Player/Skills/Loadout/AoeIntervalSpawnLoadout.asset`):
   confirm the child cadence matches expectations after setting rate + cost.

## Acceptance criteria
- Every travel-spawn child skill has an intentional `spawnEnergyCost`.
- Every travel trigger has an intentional `energyPerSecond`.
- In-play cadence matches `spawnEnergyCost / energyPerSecond`.

## Dependencies
002 (fields must exist in code before the inspector can author them).

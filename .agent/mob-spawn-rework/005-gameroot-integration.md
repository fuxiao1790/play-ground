# 005 — GameRoot integration

**Scope:** small. **Depends on:** 004. **Blocks:** 006.

## Changes to `Assets/Scripts/Game/GameRoot.cs`
Additive only — the existing static `mobs[]` discovery/registration loop and the
`Configure(CombatRoot, PlayerRoot, MobRoot[])` signature stay intact so
`GameRootAcceptsNoSceneMobsAfterSpawnerReset` keeps passing (invariant 5).

1. Add `[SerializeField] private SpawnController spawnController;`.
2. Resolve the reference in `Awake` (self-lookup is allowed there):
   ```csharp
   if (spawnController == null)
       spawnController = FindAnyObjectByType<SpawnController>();
   ```
3. **Call `Bind` in `Start`, not `Awake`** — handing `combatRoot`/`player.transform` to another
   MonoBehaviour is cross-object work, which the Awake-vs-OnEnable/Start boundary
   (`Docs/coding-standards.md`) keeps out of `Awake`. This also matches GameRoot's existing
   `Start` (it already binds the player's `SkillDriver` there,
   [GameRoot.cs:95-103](../../Assets/Scripts/Game/GameRoot.cs#L95-L103)):
   ```csharp
   spawnController?.Bind(combatRoot, player != null ? player.transform : null);
   ```
   `Bind` only fills nulls, so a fully Inspector-wired controller is untouched (invariant 4).

## Notes
- Any pre-placed static scene mobs continue to work exactly as before (independent of the
  spawner). The controller manages only mobs it spawns.

## Acceptance criteria
- A scene with a wired `SpawnController` and zero static mobs spawns via the controller.
- A scene with static mobs and no controller behaves exactly as today.
- Existing GameRoot PlayMode tests still pass.

# 008 - Verify migration, runtime flow, UI, and regressions

## Change

Add focused EditMode and PlayMode coverage, then run existing suites.

EditMode cases:

- legacy migration equivalence for root/chain/repeated-set shapes;
- runtime deep clone does not mutate templates and clones repeated sets per node;
- unbounded core supports more than three nodes/supports;
- root derivation from outgoing triggers and empty gaps;
- support/trigger compatibility and stable rejection codes;
- invalid/duplicate catalog entries;
- candidate failure preserves revision and state;
- cooldown mapping/reset/preservation policies.

PlayMode/UI cases:

- all skill/support/trigger controls open correct picker;
- valid selection compiles and affects future cast only;
- invalid choice is disabled and cannot commit;
- root cooldown blocks skill/support editing;
- trigger edits may change root topology and new root starts cooling down;
- triggered gray state and cooldown overlays refresh;
- pointer click does not attack;
- modal blocks player controls but does not change `Time.timeScale` or stop world
  simulation;
- Escape/backdrop closes and input flags always release.

Run compile, focused tests, full EditMode, and full PlayMode suites. Inspect
Profiler/GC allocation for idle bar and cooldown update.

## Acceptance criteria

- All new and existing skill/combat tests pass.
- No serialized loadout/prefab/scene reference is missing after migration.
- Repository search finds no removed legacy topology symbols or temporary
  migration path.
- Idle UI and cooldown refresh allocate zero GC per frame.
- Documentation examples match tested behavior.

## Dependencies

- 002-007 complete.

## Scope / complexity

High. Includes migration/regression confidence and UI automation.


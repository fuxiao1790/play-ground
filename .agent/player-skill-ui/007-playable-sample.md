# 007 - Assemble playable three-node sample

## Change

Create a dedicated sample scene/setup based on the current gameplay bootstrap,
but use a purpose-built valid three-node loadout and `SkillUiCatalog`.

Sample must demonstrate without inspector edits:

1. three independent roots (no triggers);
2. one root plus a two-node chain;
3. one three-node chain;
4. empty skill/support/trigger positions;
5. live valid edit affecting future casts;
6. cooldown-blocked root skill/support edit;
7. invalid picker choices disabled with reason;
8. world continuing while modal blocks player controls.

Do not attach the three-node view to `BenchmarkHybridLoadout`; that asset has
more nodes and would create invisible active skills. Add a short sample usage and
expected-results doc.

## Acceptance criteria

- Sample opens through Unity with all references assigned and no console errors.
- Bar is bottom-center and picker is top-center across common aspect ratios.
- All three topology examples can be reached through picker edits.
- Clicking UI never fires a skill.
- `Time.timeScale` remains unchanged while picker is open.
- Existing benchmark scene/loadout behavior is not silently reduced or replaced.

## Dependencies

- 006 functional UI.

## Scope / complexity

Medium.


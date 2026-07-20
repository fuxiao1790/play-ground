# 001 - Document skill UI contracts first

## Change

Write authoritative docs before runtime/UI code:

- UX reference for three visible skills, two adjacent triggers, three supports
  per skill, empty states, gray triggered nodes, cooldown overlays, and picker
  behavior.
- Game-logic contract for normalized loadout nodes, runtime clone ownership,
  edit commands/results, validation, revisions, and cooldown rules.
- Flow doc for picker open -> eligibility evaluation -> queued edit -> staged
  compile/register -> atomic swap -> events -> view refresh.
- Update architecture, layer, flow, and skill-system indexes so new docs have an
  authority location and do not conflict with the old flat-slot description.
- Document versioned save DTO (`skillId`, ordered `supportIds`,
  `triggerToNextId`) without implementing persistence.

Include one Mermaid sequence diagram and one state table for empty/root/triggered
nodes. State explicitly that UI limits do not constrain core collection sizes.

## Acceptance criteria

- Docs name one owner for authored templates, runtime equipment, cooldowns, UI
  state, and ECS snapshots.
- Docs define success/failure ordering and what every event means.
- Docs cover no-pause input behavior and future-casts-only simulation behavior.
- No authoritative doc still describes the final loadout as alternating
  `LoadoutSlot` managed references.
- Save section is design-only and cannot be mistaken for implemented behavior.

## Dependencies

None. This task must land before implementation tasks.

## Scope / complexity

Medium. Documentation-only, but architecture-critical.


# 006 - Build UI Toolkit bar, picker, and input routing

## Change

Create UXML/USS and controller/presenter for one fixed V1 view policy:

- three large skill buttons at bottom center;
- three smaller support buttons above every skill;
- two trigger buttons/arrows between neighboring skills;
- `+` empty state, selected label/icon, root cooldown overlay, and gray
  triggered-only state;
- top-center modal picker with title, scroll/grid entries, Clear/None, disabled
  reason, keyboard focus, pending state, Escape, and backdrop cancel.

Controller reads runtime loadout/revision events and three driver cooldown states.
It builds picker eligibility through `EvaluateEdit`; it sends only edit commands.
No gameplay rule is duplicated in USS/presenter code. UI policy alone enforces
three visible nodes, two links, three support positions, and no duplicate support
in one node.

Input behavior:

- never set `Time.timeScale`;
- while modal is open, tell `PlayerRoot` to block movement, dash, and attack while
  UI navigation/click/cancel remain active;
- while pointer is over interactive bar/picker, block Attack so the opening click
  cannot cast;
- release both flags on close, disable, destroy, or binding loss.

## Acceptance criteria

- Clicking any of 14 positions opens correct picker context, even when current
  slot is empty/triggered.
- All catalog entries appear; incompatible ones are disabled with reason.
- Accepted edit shows pending then refreshes from `LoadoutChanged`; rejection
  keeps prior visuals and shows reason.
- Triggered skill is gray but editable. Direct root shows correct cooldown.
- Mouse and keyboard work. Focus order is deterministic and structure does not
  prevent later gamepad support.
- UI has no per-frame allocations beyond three primitive cooldown updates.
- CPU combat/GPU VFX continue while picker is open.

## Dependencies

- 004 edit/runtime contract.
- 005 catalog.

## Scope / complexity

High. First runtime UI Toolkit surface in project.


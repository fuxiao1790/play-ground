# 005 — Editor Wiring & Verification (user steps)

These are Unity Editor actions. Per project convention, editor work is performed
by the user; this file is the checklist.

## Wiring
1. On the `GameUI` GameObject (already has `UIDocument` + `SkillLoadoutUi`), add
   the `GameplayInputSurface` component.
2. Assign its `playerRoot` field to the player's `PlayerRoot` (or rely on the
   `FindAnyObjectByType` fallback; explicit assignment preferred per DI rule).
3. Confirm `SkillLoadoutUi.playerRoot` is still assigned (unchanged).

## EventSystem / pointer delivery check
UI Toolkit runtime panels receive pointer events only via an `EventSystem` +
`InputSystemUIInputModule` (or a Unity 6 equivalent). The scene currently shows
no `EventSystem`.
- If the skill bar buttons **already** respond to clicks in play mode, delivery
  works and the surface will receive `PointerDownEvent` the same way — nothing to
  add.
- If buttons do **not** respond, add an `EventSystem` with
  `InputSystemUIInputModule` to the scene. (This would mean UI clicks never
  worked, which also explains part of the current behavior.)

## Playtest verification
- Click a skill bar button → picker opens, **player does not fire**, movement
  frozen while picker open.
- Click empty world and hold → player fires continuously; release → stops.
- Hold fire, drag cursor across the bar, keep holding → fire continues (pointer
  capture); release anywhere → stops.
- Edit a skill whose slot is on cooldown via the picker → the edit is no longer
  blocked just because clicking the bar had re-triggered fire (the
  `IsCooldownBlocked` path is now only hit by genuine recent fires).
- Aim still tracks the mouse whether or not fire is held.

## Acceptance Criteria
- All playtest checks pass.
- No regression in movement, dash, or aim.

## Dependencies
001–004 implemented.

## Scope
User editor + playtest.

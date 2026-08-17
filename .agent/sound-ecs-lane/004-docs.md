# 004 — Update the shipped sound documentation

The shipped plan already wrote `Docs/contracts/sound-events.md` and updated the
maintained doc maps. This task revises them for the lane rather than writing them
fresh — read what is there before editing.

## Change

### Update: `Docs/contracts/sound-events.md`

The shipped doc describes a single managed producer. Revise:

- **Produced By** — spawn expansion and arming systems via `SoundEmit.Enqueue`
  into `SoundEventSingleton.Events.AsParallelWriter()`. Name the four sites.
  `AudioRoot.Enqueue` remains the managed entry point, currently with no
  in-repo caller, reserved for UI/death/hurt sounds.
- **Consumed By** — `SoundEventDispatchSystem` transports; `AudioRoot` decides
  and plays. State the split explicitly: the system makes no selection decision.
- **Coverage** — replace any statement that only root casts sound. Every spawn
  sounds: root casts, interval children, on-hit spawns, stacking detonations,
  trigger-linked skills. This is the doc's most load-bearing correction.
- **Ordering** — `NativeQueue` order is meaningless under parallel writes;
  ranking is explicit. Record the resolved `PresentationSystemGroup` vs
  `LateUpdate` order from [001](./001-sound-event-lane.md), since a reader
  otherwise cannot tell whether ECS events rank in-frame.
- **Lifetime** — the lane is cleared every frame including early-outs, in
  addition to the pending batch.
- **Known behavior** — an armed AOE sounds at arm completion, not at placement,
  mirroring `AoeSpawnExpansionSystem.cs:104`. A volley or echo emits per spawned
  entity and is collapsed by the per-clip frame cap rather than by emitting once.
  A mana-rejected cast is silent, because the gate runs before expansion.
- **Restrictions** — keep the existing ones; add that managed producers must not
  write the native lane directly, cross-referencing
  `.agent/burst-onupdate-work/index.md` §*Why the dual event path stays* for the
  safety-contract reasoning.

### Update: `Docs/coding-standards.md`

- §*System Encapsulation* — add `SoundEventSingleton` to the canonical
  combat-lane examples list. The shipped plan deliberately did **not** do this,
  because sound had no lane; now it does.

### Update: `Docs/layers/presentation-and-feedback.md`

- §*Inputs* — add the sound event lane beside the pending batch.
- §*Main Systems / Modules* — add
  `Assets/Scripts/System/Audio/SoundEventDispatchSystem.cs`.

### Check, do not assume

`Docs/reference/game-logic/skill-system.md` and `Docs/folder-structure.md` were
updated by the shipped plan. Re-read them and correct only what the lane
changes — the authoring chain itself is unchanged, so most of it still stands.

## Acceptance Criteria

- No doc still says sound plays only for root casts.
- `Docs/contracts/sound-events.md` names both producer paths and states which
  one has callers today.
- The resolved presentation/`LateUpdate` ordering is written down.
- A reader who never opens `.agent/` can find out why the lane exists now when
  the shipped design deliberately omitted it.
- No doc duplicates content the shipped pass already wrote correctly.

## Dependencies

[001](./001-sound-event-lane.md) for the ordering answer; can land alongside or
after [003](./003-emit-at-expansion.md).

## Scope

Small — one substantive revision plus three targeted edits.

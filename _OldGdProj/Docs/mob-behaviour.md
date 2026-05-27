# Mob Behaviour Design

## Summary

Mobs should use a new behaviour state machine to decide intent. The existing
animation state machine in `scripts_cs/Mob/Mob.cs` stays intact and continues to
drive `MobAnimator` requests. This keeps AI decisions, animation playback,
movement, and root-node orchestration in separate places.

Each mob owns its own local event intake. Sensors, combat calls, timers, and
other mob-local systems push typed events into that intake, and the mob drains
those events during its physics update. There is no global event bus in the
first version.

Concrete behaviour is built from small C# resources listed on each mob scene. A
mob combines triggers, such as target distance or damage taken, with behaviours
such as keep-distance, swarm-target, bob-and-weave, or panic. The mob scene maps
trigger keys to behaviour keys, and the resources own the movement or reaction
details.

## Goals

- Give spawned mobs active behaviour without putting AI decisions in
  `scripts_cs/Mob/Mob.cs`.
- Keep the mob root script focused on orchestration, matching the player system
  pattern described in [`coding-standards.md`](./coding-standards.md).
- Create a second [`StateMachineCore`](../scripts_cs/Common/StateMachineCore.cs) instance
  for behaviour transitions, separate from the current animation FSM instance.
- Make behaviour changes event-driven so future sensors, attacks, status
  effects, and spawn rules can request state changes without directly owning the
  FSM.
- Make concrete behaviours scene-composable so mobs can be assembled from
  reusable trigger resources, behaviour resources, and trigger-to-behaviour maps.

## Behaviour States

V1 mob behaviour uses these states:

- `IDLE`: the mob is active but has no movement intent.
- `WANDER`: the mob moves using simple roaming logic.
- `CHASE`: the mob moves toward the current target.
- `HURT`: the mob has taken damage and briefly interrupts normal behaviour.
- `DEAD`: the mob is no longer interactive and is removed or waits for a death
  animation when one exists.

The behaviour FSM is distinct from the current animation FSM. It has its own
state enum, transition rules, and driver. Behaviour states map to animation
requests, but gameplay logic must not call `AnimatedSprite2D.play()` directly.

## Two FSMs

Mob runtime should contain two independent FSM instances:

- Existing animation FSM: already created by `scripts_cs/Mob/Mob.cs`; responsible
  for animation state transitions such as `IDLE`, `WALK`, and `HURT`; should not
  be removed, renamed, or repurposed.
- New behaviour FSM: added for AI intent; responsible for behaviour state
  transitions such as `IDLE`, `WANDER`, `CHASE`, `HURT`, and `DEAD`.

The behaviour FSM may request transitions on the animation FSM through root-owned
callbacks, but the animation FSM must not own behaviour decisions.

## Behaviour Composition

Each kind of concrete behaviour should live in its own resource class. Examples:

- `KeepDistanceBehaviour`: moves toward a target when too far away and away from
  it when too close.
- `SwarmTargetBehaviour`: moves directly toward the current target and accepts
  crowd-friendly offsets later.
- `BobAndWeaveBehaviour`: adds lateral or oscillating movement while advancing.
- `PanicBehaviour`: flees from the current threat or moves erratically.
- `WanderBehaviour`: chooses simple roaming movement when no target is active.

Behaviour resources should be small and reusable. They should not transition the
FSM, play animations, inspect unrelated game systems, or decide when they become
active. Given mob context and delta time, a behaviour resource returns movement
intent that the mob root can apply.

Each mob scene must explicitly assign its available `behaviours`. The root fails
fast when no behaviours are assigned, when the trigger map is
empty, or when a trigger maps to a missing behaviour key.

The current selector runs one primary behaviour resource at a time. For example, a
bat can map `on_target_seen` to `swarm_target`, `on_weave_range` to
`bob_and_weave`, and `on_low_hp` to `panic`.

## Triggers

Triggers decide when events should be emitted or when a trigger key should
become active. They are separate from behaviour resources so the same behaviour can
be reused with different activation rules.

Initial trigger examples:

- target enters detection radius -> emit `TARGET_SEEN` and activate
  `on_target_seen`
- target leaves detection radius -> emit `TARGET_LOST`
- target distance crosses a configured threshold -> activate
  `on_close_to_target`
- target enters weave range -> activate `on_weave_range`
- health drops below a configured percentage -> activate `on_low_hp`
- hurt timer completes -> emit `RECOVERED`

Triggers should produce events or trigger-selection hints only. They should not
call `StateMachineCore.TransitionTo()` directly and should not name concrete
behaviour resources.

## Events

Mob events are local typed values. A minimal event should include:

- `type`: the event kind.
- `source`: optional node or object that produced the event.
- `payload`: optional dictionary for event-specific data.

Initial event types:

- `TICK`: emitted each physics frame after sensors have updated.
- `TARGET_SEEN`: emitted when a sensor finds a chase target.
- `TARGET_LOST`: emitted when the current target is no longer valid or visible.
- `DAMAGED`: emitted when `take_damage()` reduces health but does not kill the
  mob.
- `RECOVERED`: emitted when a temporary interrupt state, such as `HURT`, can
  return to normal behaviour.
- `DIED`: emitted when health reaches zero.

Events should be drained in order once per physics frame. If multiple events
arrive in one frame, terminal lifecycle events win: `DIED` should leave the mob
in `DEAD` and prevent later events from returning it to a live state.

## Responsibilities

`Mob` remains the root coordinator:

- creates the new behaviour FSM, event queue, state driver, sensors, and
  movement helper
- keeps the existing animation FSM wiring intact
- wires behaviour-state transitions to animation-state requests where needed
- defines update order in `_PhysicsProcess()`
- exposes public API such as `take_damage()`
- applies final rigid-body velocity

`MobEvent` is the lightweight event value passed between local subsystems.

`MobEventQueue` stores pending events for one mob and exposes a drain method for
the root update loop.

`MobStateDriver` consumes drained events, applies transition rules, and calls
`transition_to()` on the behaviour FSM only. It owns state decision logic and
keeps that logic out of the root script.

`MobBehaviour` is the common base resource for reusable behaviours. Each
implementation owns one movement or reaction style, such as wandering, swarming,
keeping distance, bobbing and weaving, or panicking.

`MobBehaviourSelector` chooses which behaviour resource is
active by resolving the highest-priority trigger key through the mob scene's
`trigger_behaviour_map`. It keeps behaviour selection out of the root script.

Sensors and triggers produce events or selection hints only. A detection sensor
should not transition the FSM itself; it should emit `TARGET_SEEN` or
`TARGET_LOST`.

## Runtime Flow

1. Sensors and combat calls push typed events into the mob-local event queue.
2. `Mob._PhysicsProcess()` adds a `TICK` event for the frame.
3. The root drains events and passes them to `MobStateDriver`.
4. `MobStateDriver` transitions the new behaviour FSM.
5. Trigger context is resolved through `trigger_behaviour_map` to choose the
   active behaviour resource.
6. The active behaviour resource computes movement intent.
7. The mob root applies rigid-body velocity and lets Godot solve collisions.
8. Behaviour transition callbacks request animation changes through the existing
   animation FSM and `MobAnimator` wiring.

## Animation Mapping

The implementation maps behaviour states to existing mob animations:

- `IDLE` -> `MobAnimator.State.IDLE`
- `WANDER` -> `MobAnimator.State.WALK`
- `CHASE` -> `MobAnimator.State.WALK`
- `HURT` -> `MobAnimator.State.HURT`
- `DEAD` -> remove the mob with `queue_free()` until a death animation exists

The existing animation FSM remains responsible only for animation state.
Behaviour code requests behaviour state changes; root-owned callbacks translate
selected behaviour transitions into animation FSM transitions.

## Transition Rules

The implementation keeps transition rules small:

- spawned mobs start in `IDLE`
- `TICK` can move an idle mob into `WANDER`
- `TARGET_SEEN` moves `IDLE` or `WANDER` into `CHASE`
- `TARGET_LOST` moves `CHASE` back to `WANDER`
- `DAMAGED` moves any live state into `HURT`
- `RECOVERED` moves `HURT` back to `CHASE` when a target is still valid, or
  `WANDER` otherwise
- `DIED` moves any state into `DEAD`
- `DEAD` ignores all later events

Within a live state such as `WANDER` or `CHASE`, triggers may swap the active
behaviour resource without changing the FSM state. For example, a `CHASE` mob can
switch between trigger-mapped swarm, keep-distance, and bob-and-weave behaviours
while still remaining in `CHASE`.

## Testing Scenarios

- Spawned mobs leave `IDLE` and enter `WANDER` without external calls.
- Mobs enter `CHASE` when the player is detected.
- Mobs leave `CHASE` when the player is lost or invalid.
- Calling `take_damage()` emits `DAMAGED` and moves a living mob into `HURT`.
- Reducing health to zero emits `DIED`, moves the mob into `DEAD`, and removes
  it or waits for a future death animation.
- Animation follows behaviour state without gameplay code calling sprite
  playback directly.
- A mob can switch between reusable behaviours through triggers without
  replacing the behaviour FSM or animation FSM.
- A mob scene with missing behaviour composition fails fast with a clear error.
- A trigger map entry pointing to a missing behaviour key fails fast with the
  missing key named.
- Multiple spawned mobs process their own events independently.

## Future Extensions

- Add attack, cooldown, stunned, flee, or guard states after basic locomotion and
  damage are working.
- Replace simple target sensing with line-of-sight checks when level collision
  rules are ready.
- Add data-driven trigger and behaviour parameters per mob scene, such as
  detection radius, preferred distance, wander radius, hurt duration, and chase
  speed.
- Support behaviour stacks or weighted behaviour selection if one primary resource
  plus modifiers is not expressive enough.
- Add a death animation path before `queue_free()` once mob art supports it.

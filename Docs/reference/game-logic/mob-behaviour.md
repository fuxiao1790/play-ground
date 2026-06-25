# Mob Behaviour

All docs in `Docs/` are design references. They describe current intent, not
final decisions, and should be revisited in detail before implementation locks in.

## Summary

Mobs use a behavior state machine to decide intent. Animation remains separate.

Each mob owns local events. Sensors, combat calls, timers, and triggers push typed events into that mob only. There is no global event bus for the first Unity port.

Concrete behavior is built from reusable ScriptableObjects:

- behaviors compute movement intent
- triggers emit events or request behavior keys
- mob prefab maps trigger keys to behavior keys

## Goals

- keep `MobRoot` focused on orchestration
- keep AI decisions outside animation code
- keep behavior FSM separate from animation state
- make mob behavior scene/prefab composable
- fail fast when behavior setup is incomplete

## Behavior States

Initial states:

- `Idle`
- `Wander`
- `Chase`
- `Hurt`
- `Dead`

Animation mapping:

- `Idle` -> idle animation
- `Wander` -> walk animation
- `Chase` -> walk animation
- `Hurt` -> hurt animation
- `Dead` -> death transition and cleanup now, death animation later

## Two State Machines

Each mob has:

- animation FSM: owns animation state only
- behavior FSM: owns AI intent state only

Behavior transitions can request animation changes through root-owned callbacks. Animation must not make AI decisions.

## Events

Initial mob events:

- `Tick`
- `TargetSeen`
- `TargetLost`
- `Damaged`
- `Recovered`
- `Died`

Events drain in order once per fixed update.

Terminal lifecycle event wins:

- if `Died` occurs, mob enters `Dead` and ignores later live-state events

## Transition Rules

- spawn starts in `Idle`
- `Tick`: `Idle -> Wander`
- `TargetSeen`: `Idle/Wander -> Chase`
- `TargetLost`: `Chase -> Wander`
- `Damaged`: any live state -> `Hurt`
- `Recovered`: `Hurt -> Chase` if target valid, otherwise `Hurt -> Wander`
- `Died`: any state -> `Dead`
- `Dead`: ignore later events

## Behavior ScriptableObjects

Initial behaviors:

- `WanderBehaviour`: random roaming
- `SwarmTargetBehaviour`: direct chase
- `KeepDistanceBehaviour`: approach when far, back off when too close
- `BobAndWeaveBehaviour`: chase plus lateral motion
- `PanicBehaviour`: flee or jitter movement

Behavior objects should:

- read context
- return movement intent
- avoid changing FSM directly
- avoid playing animations
- avoid mutating unrelated systems

## Trigger ScriptableObjects

Initial triggers:

- `TargetSensorTrigger`: detects player within radius, loses beyond larger radius
- `DistanceBehaviourTrigger`: requests close-range behavior
- `BobAndWeaveTrigger`: requests weave behavior in range band
- `PanicTrigger`: requests low-health behavior
- `HurtRecoveryTrigger`: emits recovered after timer

Triggers should produce events or behavior-selection hints. They should not transition the FSM directly.

## Mob Runtime Pieces

`MobRoot`:

- validates prefab setup
- owns health, death transition, and cleanup scheduling
- owns Rigidbody2D velocity application
- creates behavior FSM and animation FSM
- drains local event queue
- updates triggers
- asks selected behavior for movement intent
- applies final movement

`MobBlackboard`:

- target reference
- target visibility
- health
- active trigger key
- active behavior key
- per-mob status stack state

`MobStateDriver`:

- consumes events
- applies transition rules
- keeps state decisions out of root

`MobBehaviourSelector`:

- resolves active trigger key through trigger-behavior map
- returns selected behavior

## Tests To Port

- spawned mob leaves `Idle` and enters `Wander`
- target detection enters `Chase`
- lost target returns to `Wander`
- damage enters `Hurt`
- recovery returns to `Chase` or `Wander`
- lethal damage enters `Dead`
- dead mob stops targeting and movement
- missing behavior setup fails immediately and clearly
- missing trigger map key fails immediately and clearly
- multiple mobs process independent local events

# Coding Standards

## Root Script Rule

Each gameplay object should have a root script attached to its root scene node.

Examples:

- player
- mob
- weapon
- interactable

The root script should act as the coordinator for that object. It should not contain the object's gameplay logic directly.

## What The Root Script Should Do

- own references to child nodes and subsystem classes or resources
- wire subsystems together in `_Ready()`
- define update order in `_PhysicsProcess()` or `_Process()`
- register transition callbacks and signal wiring between subsystems
- expose the object's public API to the rest of the game

## What The Root Script Should Not Do

- implement movement math directly
- implement combat rules directly
- implement animation selection directly
- decide gameplay state directly from gameplay data
- mix multiple gameplay concerns into one large scene script

Gameplay logic should live in focused subsystem classes or resources that the root script composes and coordinates.

Preferred root-script logic looks like wiring:

- subscribing to state-machine transition callbacks
- connecting signals
- creating subsystem instances
- defining subsystem update order

Non-preferred root-script logic looks like decision-making:

- checking movement state and deciding whether the object is `IDLE` or `MOVING`
- converting gameplay inputs into combat state
- embedding rules that belong to movement, AI, combat, or state subsystems

## Fail Fast Validation

Code should use a fail-fast pattern instead of spreading defensive null checks through gameplay logic.

Exported fields and required node references must be validated immediately during setup, before any game logic runs. If a required dependency is missing or invalid, report it clearly and stop the object from continuing into gameplay behavior.

If a class cannot function without a field, treat that field as required configuration instead of optional state. Do not make required exported references or required node dependencies nullable with `?` just to satisfy setup timing, then carry that nullability through the rest of the class.

Preferred pattern:

- keep required dependencies as non-optional in the gameplay-facing API
- validate or resolve them once during setup
- fail immediately with a clear error when they are missing

Non-preferred pattern:

- declaring a required field as `Node2D?`, `Timer?`, `PackedScene?`, or similar
- using `field == null`, `field?....`, or other repeated null guards during active gameplay just because setup may have been wrong
- letting a misconfigured scene continue running in a degraded or half-wired state

Do not hide invalid scene configuration with repeated `if (value == null)` checks inside `_Process()`, `_PhysicsProcess()`, state updates, AI decisions, or combat logic. Validate once at the boundary, then write the gameplay code as though its required dependencies are present.

## Avoid Runtime Reflection

Gameplay code should avoid runtime reflection and metadata-driven lookup on hot or repeated paths.

Do not use APIs such as `System.Reflection`, `Type.GetType`, `GetType().GetMethod()`, `Enum.GetName`, `Enum.Parse`, or enum `ToString()` for gameplay decisions, state labels, or per-frame debug display. Prefer explicit switches, typed references, dictionaries built during setup, or small helper methods that make supported states obvious at compile time.

Godot scene interop should also stay explicit. Prefer typed node access, `Callable`, signals, exported references, and `StringName` constants over string-built method lookup or reflective dispatch. Reflection is acceptable only for editor tooling, tests, or isolated diagnostics where there is a clear need and no gameplay-frame cost.

## Test-Only Scene Fixtures

Godot `.tscn` files that are used only by tests must stay under [`tests/`](../tests/).
Use `tests/scenes/` for test fixture scenes instead of placing them in the runtime
[`scenes/`](../scenes/) tree.

## Keep Test Hooks Out Of Runtime Logic

Tests must verify runtime behavior through the same public contracts that
gameplay uses, or through test-only helpers that live under [`tests/`](../tests/).

Do not add production code paths, metadata writes, debug breadcrumbs, exported
flags, counters, or callback side effects solely so a smoke test can observe
that something happened. If a test needs observability, prefer:

- a typed listener or recorder implemented under `tests/`
- an existing public API on the runtime root
- a focused runtime query method that is useful outside the test as diagnostics
  or tooling

Non-preferred pattern:

- gameplay code writing `SetMeta(...)` or other dynamic state each hit, frame,
  spawn, or callback only for a test assertion
- runtime systems carrying arrays, dictionaries, or flags that no gameplay,
  tooling, or debug UI consumes
- adding branches such as `if testing` or exported test toggles to active
  gameplay paths

If the only consumer is a test, keep the observer in test code and wire it
through an existing typed boundary such as a listener, signal, or explicit
diagnostic API.

## Protect Hot Paths From Hidden Setup Work

Code that runs on repeated gameplay paths must not hide expensive one-time setup inside runtime calls.

Examples of repeated gameplay paths:

- mob spawn
- projectile spawn
- attack perform
- per-frame update
- per-physics-step update

Do not put scene instantiation, collision baking, render baking, texture inspection, or other asset-derived setup work inside calls that may run once per spawn or once per frame.

Do not put soft-timing gameplay such as cooldown ticking in `_PhysicsProcess()` unless it directly depends on fixed-step simulation. If a few milliseconds of delay does not change gameplay in a meaningful way, prefer `_Process()` or another non-physics timing path.

If code needs derived data from a `PackedScene`, `CollisionShape2D`, `SpriteFrames`, textures, or other scene assets:

- bake it once during setup
- cache it by the asset identity and any relevant configuration key
- reuse the cached result on later spawns or updates

Registration APIs should also follow this rule. A method named like `Register...`, `Setup...`, or `Configure...` must not repeatedly rebuild the same data for the same asset unless the caller explicitly asks for a rebuild.

Preferred pattern:

- resolve and bake expensive asset data once
- store the result in a cache owned by the system root or other long-lived coordinator
- let hot-path calls only reference cached ids, lightweight structs, or already-created resources

Non-preferred pattern:

- each spawned mob registers the same projectile template again
- each projectile registration instantiates the same scene again to read sprite or collision data
- each spawn point instantiates gameplay scenes during active play to measure visuals

When in doubt, assume spawn-time hitches are bugs. If a runtime path is expected to repeat during combat, design it so the repeated call does allocation-light work only.

## Debug UI Ownership

Top-left screen debug text should be owned by the dedicated debug scene script, currently [`scripts_cs/Debug/Debug.cs`](../scripts_cs/Debug/Debug.cs).

Use that shared debug layer for scene-level or game-level facts such as:

- FPS
- total active mob count
- total active projectile count
- other aggregated runtime counters that describe the whole scene

Do not let gameplay object scripts write directly to `DebugLayer/DebugLabel` or any other shared top-left screen label.

Per-object debug widgets should stay with the object that owns the data. A player, mob, weapon, or other gameplay node may draw its own local debug widget from its own script when that widget is attached to that node and describes only that node's state.

Preferred pattern:

- `Debug.cs` gathers and renders shared screen-space debug text
- individual node scripts render their own local widget or label for node-specific state

Non-preferred pattern:

- `Player.cs` or `Mob.cs` reaching into `DebugLayer/DebugLabel`
- multiple unrelated gameplay scripts competing to write the same shared debug label

## Player Example

[`scripts_cs/Player/Player.cs`](../scripts_cs/Player/Player.cs) is the reference example for the wiring style of this pattern.

It works as an orchestrator:

- it creates and stores the movement, facing, animation, and state-machine subsystems
- it wires state transitions to animation requests
- it defines the per-frame update order

The gameplay concerns are split away from the orchestration layer:

- [`scripts_cs/Player/PlayerMovement.cs`](../scripts_cs/Player/PlayerMovement.cs): movement input and velocity logic
- [`scripts_cs/Player/PlayerFacing.cs`](../scripts_cs/Player/PlayerFacing.cs): facing and rotation behavior
- [`scripts_cs/Player/PlayerStateDriver.cs`](../scripts_cs/Player/PlayerStateDriver.cs): gameplay-state decision logic for locomotion
- [`scripts_cs/Player/PlayerAnimator.cs`](../scripts_cs/Player/PlayerAnimator.cs): animation state handling
- [`scripts_cs/Common/StateMachineCore.cs`](../scripts_cs/Common/StateMachineCore.cs): reusable state-machine behavior

## Preferred Pattern For New Objects

When building a new object:

1. Create a root scene node with a single root script.
2. Keep the root script focused on composition and orchestration.
3. Move gameplay behavior into child nodes, resources, or narrowly scoped subsystem classes.
4. Let the root script wire those pieces together and define the object's public surface.

## Note On Current Implementation

The current player implementation now follows this pattern more closely:

- the root script owns wiring and update order
- transition callbacks stay in the root
- locomotion state decisions live in a dedicated subsystem

Its helper logic is currently split into focused subsystem classes rather than node-attached child scripts. For future scene objects, prefer the same separation of concerns, with child-driven composition when that fits the scene structure cleanly.

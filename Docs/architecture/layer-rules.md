# Layer Rules

## What A Layer Is

A layer is an ownership boundary. It owns specific runtime state and is
responsible for changing that state safely.

Layers may expose contracts to other layers. A layer must not reach around a
contract to mutate another layer's private state.

## Ownership

The owning layer decides:

- where state lives
- who may mutate it
- when lifecycle starts and ends
- what data leaves the layer
- what validation is required before data leaves the layer

If a rule is about ownership, put it in the owning layer doc.

## Dependencies

A dependency exists when one layer reads another layer's state, calls its API, or
uses its data contract.

Allowed dependencies must be explicit in the layer doc. Forbidden dependencies
must stay forbidden even when they seem convenient for tests or prototypes.

## Package Boundary

Core runtime code is split into asmdefs under `Assets/Scripts/` with one-way references:

```text
PlayGround.SkillUi -> PlayGround.GameLogic -> PlayGround.Sim
```

`PlayGround.Sim` (`Assets/Scripts/System/`) must not reference game logic, UI, or Debugging. The combat
bridge and ECS-serving presentation components live in Sim with the ECS runtime.
`PlayGround.Debugging` (`Assets/Scripts/Debugging/`) is an exempt leaf: it may reference core assemblies, but no
core assembly may reference it.

## Boundary Data

Data that crosses layers must be plain and documented as a contract when it is
used by more than one module. Important contract categories in this project:

- managed spawn requests
- ECS spawn events and commands
- target proxy components
- combat hit/result buffers
- VFX requests
- render batch data
- runtime skill snapshots

## Runtime, Authoring, And Configuration

Authoring data may use Unity objects such as ScriptableObjects, prefabs,
Colliders, Transforms, VisualEffectAssets, and serialized fields.

Runtime high-count ECS data must be copied from authoring data before
simulation. In-flight projectile, AOE, status, and VFX work must not read live
authoring objects.

## Managed And Unmanaged Data

Jobs and simulation systems may read unmanaged ECS components, buffers, native
containers, and target proxy data.

Jobs and simulation systems must not read GameObjects, Transforms, Colliders,
Physics2D, ScriptableObjects, or managed `TargetCompanion` values.

Only the presentation bridge may resolve managed target companions.

## Structural Changes

Hot combat churn should avoid archetype changes. Prefer enableable components
and reusable archetypes for projectiles and AOEs. Structural deletion is reserved
for root/scope teardown and target proxy lifecycle cleanup.

## Documentation Authority

- Ownership rules: layer docs.
- Cross-layer sequences: flow docs.
- Boundary data shapes: contract docs.
- Rationale and tradeoffs: decisions.
- Detailed implementation notes: reference docs.

# 004 — Expansion Command Fan-Out

**Depends on:** 001, 003
**Scope:** small

## Goal

Split the expansion system's single command output into two lists — discrete and swept —
switching on the authored flag carried in the command.

## Why the split is at the command level, not the event level

The AOE lanes split at the **event** level because their discriminator (`IntervalChildKind`)
is known to every producer. Sweep's is not: interval and on-hit children dereference their
template *inside* the expansion job
([ProjectileSpawnExpansionSystem.cs:173](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L173)),
so a producer enqueueing a `ProjectileSpawnEvent` does not yet know what the template says.
`WriteCommand` ([:248](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L248))
is the single point every spawn path converges through and where the resolved template is in
hand. That is where the switch goes.

## Changes

### `ProjectileSpawnEventSingleton`

```csharp
public NativeList<ProjectileSpawnCommand> Commands;        // discrete lane
public NativeList<ProjectileSpawnCommand> SweptCommands;   // swept lane
```

Update the type's `ECS Lifecycle:` comment to cover both lists. `PendingHandle` still covers
both — one expansion job writes both, so one handle is correct and a second would mislead.

### `ProjectileSpawnExpansionSystem`

- `OnUpdate`: dispose and null **both** lists in the existing previous-frame cleanup
  ([:90-94](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L90-L94));
  allocate both before scheduling; assign both onto the singleton.
- `OnDestroy`: dispose `SweptCommands` alongside `Commands`, guarded by `IsCreated`.

### `ProjectileExpansionJob`

Add `public NativeList<ProjectileSpawnCommand> SweptCommands;`. At the end of
`WriteCommand`, replace the single `Commands.Add(command)`:

```csharp
if (command.SweptCollision != 0)
{
    SweptCommands.Add(command);
}
else
{
    Commands.Add(command);
}
```

No config read, no math, no `SystemAPI` access inside the job. The flag was decided at
authoring time and resolved at skill-compile time; expansion only dispatches on it.

## Acceptance Criteria

- One `ProjectileSpawnEvent` queue remains; no new event type, no new event singleton.
- Both command lists are allocated, assigned, and disposed on **all three** exit paths:
  no events, no template registry
  ([:138-143](../../Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs#L138-L143)),
  and the normal path.
- Routing reads `command.SweptCollision` only — no speed, radius, or timestep input.
- A command whose template is swept lands in `SweptCommands` for all four spawn paths.

## Risks

- **Disposal paths.** Three exits touch `Commands`. Missing `SweptCommands` on any of them
  leaks or double-disposes. Walk all three.

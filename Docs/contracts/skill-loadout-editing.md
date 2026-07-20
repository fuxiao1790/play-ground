# Skill Loadout Editing

## Purpose

Define the one mutable equipment model and command boundary for player skill
editing. This contract is authoritative for runtime loadout editing. It does not
implement save/load or define UI layout.

## Ownership

| Thing | Owner | Rule |
|---|---|---|
| `Skill`, `SkillSupport`, `TriggerLink`, authored `SkillSet`, authored `SkillLoadout` | Scene And Authoring | Shared template assets. Never mutate during play. |
| Runtime `SkillLoadout` and runtime `SkillSet` clones | `SkillDriver` in Game Logic | One mutable session equipment source. |
| Validation, compilation, template registration, root cooldowns | `SkillDriver` and Game Logic helpers | UI cannot perform or bypass these steps. |
| Picker, focus, pending state, visible-slot limit | UI Toolkit presenter/controller | Presentation state only. |
| In-flight projectile/AOE values and template keys | ECS/combat runtime | Copied at cast/spawn time; not updated by loadout edits. |

## Normalized Loadout

`SkillLoadout` owns one unbounded ordered list of `SkillLoadoutNode` values:

```csharp
sealed class SkillLoadoutNode
{
    public SkillSet skillSet;          // null means empty node
    public TriggerLink triggerToNext;  // null means no outgoing link
}
```

`triggerToNext` may target only the immediately following node. It is valid only
when both neighbor nodes contain a skill set and the existing validator accepts
the link. A node's incoming link is derived from the node before it. A node with
an incoming link is triggered-only; a node without one is a direct-cast root.
This is forward-only topology. Core node and support collections have no UI-size
limit.

The player may start with no authored loadout. In that case `SkillDriver` creates
an empty runtime loadout; choosing a skill at a UI position grows the runtime
node list through that position. This does not create or modify an authored
`SkillLoadout` asset.

| Node state | Stored facts | Cast/edit meaning | UI meaning |
|---|---|---|---|
| Empty | `skillSet == null` | No cast. Skill may be selected. | `+` skill position; support and adjacent-link actions explain dependency. |
| Root | Skill set, no incoming link | Direct-cast. Driver owns cooldown and may reject skill/support edits during cooldown. | Normal skill treatment plus cooldown overlay. |
| Triggered | Skill set, incoming link | Never direct-cast. It may be edited because it has no direct-cast cooldown. | Gray triggered-only treatment; still selectable. |

## Command Boundary

UI creates `SkillLoadoutEditCommand` values only. It never changes a `SkillSet`,
`SkillLoadout`, cooldown, compiler result, or combat registration directly.

```csharp
enum SkillLoadoutEditKind
{
    SetSkill, ClearSkill, SetSupport, ClearSupport, SetTrigger, ClearTrigger
}

struct SkillLoadoutEditCommand
{
    public ulong expectedRevision;
    public SkillLoadoutEditKind kind;
    public int nodeIndex;
    public int supportIndex;
    public string definitionId;
}

struct SkillLoadoutEditResult
{
    public bool accepted;
    public ulong revision;
    public string rejectionReason;
}
```

`definitionId` is resolved by a future explicit `SkillUiCatalog`, not by
editor-only asset search. `supportIndex` is a UI slot address; the core support
list remains unbounded. Commands include `expectedRevision` so stale picker
state is rejected rather than overwriting a newer edit.

One edit may be pending. At the start of `SkillDriver.Tick`, the driver:

1. rejects a stale revision or cooldown-disallowed command;
2. deep-clones the current runtime loadout and every referenced runtime
   `SkillSet` needed by the candidate;
3. applies exactly one command to the candidate;
4. checks node dependencies and calls `SkillLoadoutValidator` for compatibility;
5. compiles and registers the candidate into staging data;
6. swaps the runtime loadout, compiled roots, and mapped cooldown state together;
7. increments the revision and publishes success events.

Any failure before step 6 leaves the old runtime loadout and compiled roots
active. Candidate state is discarded. New root nodes start at zero cooldown
progress; unchanged roots preserve their progress; an accepted skill/support
edit on a direct root resets that root's progress. Trigger commands remain
allowed while a root cooldown runs.

## Events

The driver emits events after a completed command attempt, on Unity's main
thread:

| Event | Meaning | Ordering |
|---|---|---|
| `LoadoutChanged(revision)` | A complete runtime loadout and compiled-root swap succeeded. | Published first after swap. |
| `EditResolved(result)` | The pending command has succeeded or failed. | Published after `LoadoutChanged` on success; alone on failure. |
| `CooldownStateChanged` | A root cooldown value changed for presentation. | May occur during ordinary ticks; does not imply an edit. |

UI releases its pending state only after `EditResolved`. A successful refresh
reads the new revision after `LoadoutChanged`; a failed refresh retains the old
loadout and shows `rejectionReason`.

## Eligibility Rules

- Picker lists all catalog choices. A choice that validator rules reject is
  disabled with its reason.
- Duplicate support selection is a v1 UI-policy rejection; it does not cap or
  change the core support model.
- Clearing a skill is disabled until its supports and adjacent dependent links
  are cleared.
- Skill/support changes to a direct root are disabled while that root's cooldown
  is running. Trigger changes are still allowed.
- Invalid state never commits and no command auto-clears a different slot.

## Save DTO

Persistence maps the normalized topology to baked Unity asset GUID strings and
does not serialize runtime clones, compiled definitions, cooldown state, or
Unity object references:

```csharp
sealed class PlayerSkillLoadoutSaveV1
{
    public int version = 1;
    public List<Node> nodes;

    sealed class Node
    {
        public string skillAssetGuid;
        public List<string> supportAssetGuids;
        public string triggerToNextAssetGuid;
    }
}
```

An empty string encodes an empty field. GUIDs are baked from Unity's asset
database into `Skill`, `SkillSupport`, and `TriggerLink` assets so catalog
reordering does not alter save identity. Load resolves GUIDs from the authored
default loadout and catalog, then validates before session runtime clones are
created. See [Player Save Data](./player-save-data.md).

## Related Documents

- [Player Skill UI](../reference/design/player-skill-ui.md)
- [Skill Loadout Edit Flow](../flows/skill-loadout-edit.md)
- [Game Logic](../layers/game-logic.md)
- [Skill Runtime Snapshots](./skill-runtime-snapshots.md)

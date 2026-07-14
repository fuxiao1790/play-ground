# Hit Payload Lane Decouple

Caveman words: projectile hit-data and AOE hit-data share one struct
(`CombatHitPayload`). Rest of combat keep projectile lane and AOE lane apart —
own events, own systems, own commands. This one shared struct is odd one out.
Split it: projectile own its hit-data, AOE own its hit-data. Same fields today,
but no more shared type tying two lanes together. Behavior no change.

## Why (and why now)

This is the **prerequisite** for the follow-on "hit event carries source entity,
finalize reads payload from source" refactor. That refactor wants finalize to
index the source entity and read *that lane's* payload component directly. As
long as both lanes funnel through one shared `CombatHitPayload`, "read the
source's payload" is entangled across lanes. Decouple the payload type first;
then each lane's read is self-contained.

It also removes the last runtime data type shared across the projectile/AOE lane
split that the codebase otherwise maintains everywhere (separate spawn events,
separate collision systems, separate commands — see [[feedback_split_lane_at_producer]]).

## Current coupling

One shared struct `CombatHitPayload` ([CombatHitPayload.cs](../../Assets/Scripts/System/Application/CombatHitPayload.cs)):
`DamageAmount, CritChance, CritMultiplier, DirectDamageEnabled, SourceNodeId, StackEffect`.

Both lanes embed the identical struct (and both pair it with `OnHitSpawnRef`):

- **Projectile:** `ProjectileHitComponent.HitPayload` is `ProjectileHitPayload`,
  a readonly wrapper `{ CombatHitPayload HitPayload; OnHitSpawnRef OnHitSpawn; }`
  ([ProjectileRuntimeEvents.cs:15-35](../../Assets/Scripts/System/Projectiles/ProjectileRuntimeEvents.cs#L15-L35)).
- **AOE:** `AoeHitSpawnComponent.HitPayload` is `CombatHitPayload` directly, with
  `OnHitSpawnRef` as a sibling field
  ([AoeEcsComponents.cs:41-45](../../Assets/Scripts/System/Aoes/AoeEcsComponents.cs#L41-L45)).

## Target shape (decoupled)

- **`ProjectileHitPayload`** owns the six fields directly (inline; drop the nested
  `CombatHitPayload`). Keeps `OnHitSpawnRef OnHitSpawn`. Keeps existing convenience
  accessors as readonly properties over the inlined fields.
- **New `AoeHitPayload`** owns the same six fields. `OnHitSpawnRef` stays a
  sibling field on `AoeHitSpawnComponent`/`AoeSpawnCommand`, exactly as today
  (preserve the existing per-lane asymmetry — do not fold OnHitSpawn into the AOE
  payload).
- **`CombatHitPayload` is deleted.** No shared payload container remains.
- `StackEffectSnapshot`, `EntityId`, `OnHitSpawnRef` stay shared — those are
  genuine cross-domain types, not the payload container. Decoupling the container
  does not touch them.

`CombatHitEvent` is **unchanged** by this task — it keeps its flat damage fields,
and the collision enqueue code is untouched because every `.HitPayload.DamageAmount`
/ `.HitPayload.StackEffect` read resolves identically against the new types (same
field names). The event→source refactor is the separate follow-on.

## Constraints & invariants (with source)

1. **Behavior-identical.** This is a pure type split. Field names, values, and all
   runtime logic are unchanged. No system ordering, allocation, or job-safety
   property changes. The only semantic edits are collapsing `new CombatHitPayload{…}`
   wrappers and the one double-hop `cmd.HitPayload.HitPayload`
   ([ProjectileSpawnApplySystem.cs:199](../../Assets/Scripts/System/Projectiles/ProjectileSpawnApplySystem.cs#L199)).
2. **Burst / unmanaged.** Both new payloads stay plain unmanaged structs (they sit
   inside `IComponentData` and inside spawn commands consumed in Burst jobs). No
   managed refs introduced. `SourceNodeId` (`EntityId`) and `StackEffectSnapshot`
   are already Burst-safe and unchanged.
3. **Template-reset paths.** `SpawnTemplateFor` for each lane zeroes
   `StackEffect.Faction` on the payload
   ([CombatRoot.cs:571-599](../../Assets/Scripts/System/Core/CombatRoot.cs#L571-L599)).
   Projectile currently unwraps `hp.HitPayload` (the nested `CombatHitPayload`);
   after inlining it edits the field directly. AOE already edits directly — only a
   type rename. Behavior identical.

## Mechanisms reused vs introduced

- **Reused:** the project's existing per-lane type split (projectile lane vs AOE
  lane already have their own commands/events/components). This task extends that
  same pattern to the payload type.
- **Introduced:** `AoeHitPayload` (new), and `ProjectileHitPayload` changes from a
  wrapper to a fields-owning struct. See the structural-warning note below.

## Minimal/additive vs refactor comparison

The plan-changes methodology flags "a second data type for the same concept" as a
**structural warning** — and decoupling deliberately creates two structs with
identical field lists today. Addressed head-on:

- **Keep shared (`CombatHitPayload`):**
  - data flow: both lanes read one type
  - long-term cost: the two lanes stay coupled through this one type; the follow-on
    source-lookup refactor must read a shared type per lane; the payload cannot
    evolve per-lane (an AOE-only or projectile-only field forces a shared-type
    change that leaks into the other lane); it is the lone shared runtime type
    against an otherwise fully lane-split design.
- **Decouple (this plan):**
  - data flow: each lane reads its own type
  - copies/translations removed: the `ProjectileHitPayload → CombatHitPayload`
    unwrap hop; the `cmd.HitPayload.HitPayload` double access
  - long-term benefit: lanes evolve independently; the follow-on refactor reads
    each lane's own component with no shared-type entanglement; consistent with the
    established lane split
  - honest cost: two structs share a field list today (duplicated field
    declarations)

**Decision: decouple — explicit user decision.** The duplicated field list is the
accepted cost; the benefit is lane independence and unblocking the source-lookup
refactor. This is a deliberate divergence from the "one type per concept" default,
justified because projectile-hit and AOE-hit are treated as distinct concepts
everywhere else in this codebase.

## Design validation

| Invariant | Result |
|---|---|
| Behavior-identical | ✓ pure type split; field names/values unchanged |
| Burst/unmanaged | ✓ plain structs, no managed refs |
| Template reset preserved | ✓ same faction-zeroing, direct field edit |
| No system/ordering change | ✓ CombatHitEvent + collision enqueue untouched |

## Tasks

- [001-projectile-hit-payload.md](001-projectile-hit-payload.md) — inline the fields into `ProjectileHitPayload`; update projectile build/consume sites.
- [002-aoe-hit-payload.md](002-aoe-hit-payload.md) — add `AoeHitPayload`; retype AOE command + component; update AOE build/consume sites.
- [003-remove-shared-type-and-tests.md](003-remove-shared-type-and-tests.md) — delete `CombatHitPayload`; sweep tests in both lanes; update the contract doc.

001 + 002 remove every production reference to `CombatHitPayload`; 003 deletes the
type and fixes tests. All three land in one compiling commit.

## Follow-on (separate plan, after this lands)

`hit-event-source-lookup`: shrink `CombatHitEvent` to `{ TargetProxy, Source }`;
`CombatApplyFinalizeSingleSystem` indexes the `Source` entity and reads the
now-decoupled per-lane payload directly (one `ComponentLookup[Source]` per lane,
selected by which payload component the source carries — disjoint archetypes, no
query, no loop). Removes the per-hit payload copy entirely. Validated by user-run
PlayMode profiling per [[feedback_plan_then_verify_ecs]].

## Open questions / decisions

- **`ProjectileHitPayload`: readonly-with-ctor vs plain fields.** Recommend
  converting to a **plain struct with public fields** (matching `AoeHitPayload`)
  so initializer syntax works uniformly at build/test sites and the wrapper ctor
  disappears. Keep `Enabled`/`Damage`-style helpers as readonly computed
  properties. Detailed in [001](001-projectile-hit-payload.md).

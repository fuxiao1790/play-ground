# Task Execution Packet

## Task
006-update-launch-aim-docs.md

## Goal
Update six design-reference docs so they describe the now-complete and
now-tested triggered-projectile-launch-aim feature (tasks 001-005). This is a
documentation-only task: no code or test changes. All six target files have
already been read in full during this plan's execution; this packet gives you
the exact prose to insert and where, because doc voice/precision matters and
the design is fully settled — treat the snippets below as the primary content,
adapting only for exact surrounding formatting/heading level at the insertion
point (re-read each file first to find the precise anchor text quoted below).

## Files Allowed To Modify
- Docs/reference/game-logic/skill-gameplay-system.md
- Docs/contracts/skill-runtime-snapshots.md
- Docs/contracts/spawn-events-and-commands.md
- Docs/reference/simulation/projectile-system.md
- Docs/reference/simulation/spawn-template-registry.md
- Docs/flows/spawn-event-to-entity.md

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- All six files listed above, in full (each was already read once during this
  plan's execution to determine the insertion points below — re-read to get
  exact current line numbers/surrounding text before editing, since other
  unrelated doc edits may have landed between then and now).
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (source of
  truth for the launch-aim mechanics described below — confirms behavior before
  you write it into docs).
- Assets/Scripts/Skills/Trigger/TriggerLink.cs,
  Assets/Scripts/System/Projectiles/ProjectileLaunchAimMode.cs (authoring-side
  source of truth).

## Behavior To Preserve
- Do not change any other content in these six files beyond the additions
  below. Do not "clean up" unrelated nearby text.
- Match each file's existing heading level, list style, and voice exactly
  (terse, factual, code-referencing — no marketing language, no first person).
- Do not invent behavior not already implemented in tasks 001-004 — every claim
  below is already verified true against the shipped code (see this plan's
  `implementation-log.md`, tasks 001-004 Validation Summary entries).

## Behavior To Change (Exact Insertions)

### 1. `Docs/reference/game-logic/skill-gameplay-system.md`
Insert a new subsection immediately after the existing bullet list under
`## Trigger Semantics` (i.e. right after the `StackTrigger` bullet and before
the "Root cast spends mana..." paragraph), so it reads as a fourth
trigger-adjacent concept alongside `OnHit`/`IntervalSpawn`/`StackTrigger`:

```markdown
### Projectile Launch Aim

A trigger link may additionally author a projectile launch-aim policy:
`ProjectileLaunchAimMode` (`None` or `NearestHostile`) plus a non-negative
acquisition range. This governs how the *triggered* projectile effect is
launched, not the trigger's own targeting or cost — root/player casts always
use manual aim or existing player aim assist; launch-aim policy applies only to
projectile effects reached through an incoming trigger edge, and only when the
compiled target is itself a projectile. Values are inert for AOE/targeted
targets and for the top-level/root compiled projectile.

Launch aim is independent of discrete-only homing (`Tracking`/`trackingEnabled`
on `ProjectileDefinition`): a triggered projectile may enable one, the other,
both, or neither. Launch aim is a one-time initial-velocity redirect applied at
spawn; homing is continuous re-steering after spawn, and only the discrete
projectile archetype carries a tracking component at all. See
[Projectile System](../simulation/projectile-system.md#launch-aim) for the
runtime acquisition/redirect mechanics.
```

### 2. `Docs/contracts/skill-runtime-snapshots.md`
In `## Fields / Shape`, change the existing bullet:
```markdown
- runtime projectile definitions
```
to:
```markdown
- runtime projectile definitions (includes copied trigger-authored launch-aim
  mode/range for a trigger's projectile target only; root definitions keep the
  disabled default — see
  [Skill Gameplay System](../reference/game-logic/skill-gameplay-system.md#projectile-launch-aim))
```
No other change to this file.

### 3. `Docs/contracts/spawn-events-and-commands.md`
After the paragraph beginning "Commands carry the resolved spawn data. A
command describes exactly one ECS entity with position, velocity, bounds,
identity, hit payload, lifetime, render state, and optional timed-spawn
state...", insert a new paragraph:
```markdown
`ProjectileSpawnCommand` also carries authored launch-aim policy
(`LaunchAimMode`, `LaunchAimRange`) copied from the compiled runtime projectile
definition at template-build time — it is template-level policy, not
per-instance frame, so `ProjectileSpawnEvent` carries no launch-aim field.
Expansion resolves nearest-hostile acquisition once per event/wave from the
dereferenced template, before count/spread fan-out and before the
discrete/continuous split, and redirects only one deterministic shot's
velocity. See
[Projectile System](../reference/simulation/projectile-system.md#launch-aim).
```

### 4. `Docs/reference/simulation/projectile-system.md`
Insert a new `## Launch Aim` section (with the explicit anchor other docs link
to — GitHub-flavored Markdown derives the anchor `#launch-aim` automatically
from this heading text, so no explicit anchor tag is needed) between the
existing `## Timed Child Spawns` section and `## Tracking And Movement`
section:

```markdown
## Launch Aim

Trigger-authored launch aim gives one triggered projectile wave a one-time
initial-velocity redirect toward the nearest hostile, resolved once per
`ProjectileSpawnEvent` inside `ProjectileSpawnExpansionSystem`, before
count/spread/jitter fan-out and before the discrete/continuous lane split. It
is authored on the incoming `TriggerLink`, not on `SkillDefinition`: root and
player casts never carry it, and it is copied onto the compiled
`RuntimeProjectileDefinition` only for a trigger's compiled projectile target
(interval child, on-hit target, or stack detonation), then into the
`ProjectileSpawnCommand` template alongside the other template-level fields
that participate in `SpawnTemplateHash`.

When a wave's template carries `LaunchAimMode == NearestHostile`, `Range > 0`,
and a valid faction, expansion queries the shared `TargetSpatialHashSingleton`
through `CombatTargetAcquisition.TrySelectNthNearest` (the same nearest-hostile
selection targeted chains use, rank 0, excluding the event's contact-gate seed
target) once per event. On success it replaces only deterministic shot index
0's velocity with the exact normalized direction to the target times the
template's `Speed`; every other shot in the wave keeps its normal
forward/side-spray/radial pattern velocity, and the full pattern math and RNG
draws still run for shot 0 before its velocity is overwritten, so later shots'
RNG sequence is unaffected by whether launch aim is enabled. A missing hash
singleton, non-positive range, `CombatFaction.None`, no hostile in range, or a
target coincident with the spawn position all fall through to the unmodified
pattern output.

Both the discrete and continuous lanes receive the same one-shot replacement,
since acquisition runs once per event before `WriteCommand` routes each shot by
`ContinuousCollision`. Launch aim only sets the initial velocity; it adds no
per-frame steering, target ownership, or entity state, and it does not enable
tracking — the continuous archetype still has no `ProjectileTrackingComponent`,
and a discrete projectile's separately authored tracking (see
[Tracking And Movement](#tracking-and-movement)) may still steer after an
aimed launch. `ProjectileSpawnExpansionSystem` schedules its acquisition read
dependent on `TargetSpatialHashSingleton.BuildHandle` and publishes its handle
into `ConsumerHandle`, exactly like `TargetedResolveSystem`.
```

Additionally, in `## Authoring Notes`, add one new bullet to the existing list
(after the `SkillDefinition projectile entries...` bullet):
```markdown
- `TriggerLink` (not `SkillDefinition`) authors trigger-only projectile
  launch-aim mode/range; `SkillSetCompiler` copies it onto a trigger's compiled
  projectile target only, never the root. See
  [Launch Aim](#launch-aim).
```

### 5. `Docs/reference/simulation/spawn-template-registry.md`
In `## Projectile Runtime Snapshot`, the bullet list currently reads:
```markdown
Projectile commands and component data carry:

- `ProjectileHitPayload` — hit payload with optional `OnHitSpawnRef (kind, key)`
- `TimedSpawnComponent` — optional interval-child spawn config
```
Add one more bullet to that list:
```markdown
- `LaunchAimMode` / `LaunchAimRange` — trigger-authored launch-aim policy,
  copied at compile time onto a trigger's projectile target only (never the
  root); template-level, not per-instance, so it participates in
  `SpawnTemplateHash` like `Speed` or `ContinuousCollision`. See
  [Launch Aim](../simulation/projectile-system.md#launch-aim).
```
(Note: the mojibake `—` in the existing bullets in this file may render as
`鈥?` in your editor/terminal depending on encoding — preserve whatever the
file's actual existing byte sequence is for consistency; do not "fix" the
existing bullets' encoding as part of this task, and use a plain hyphen `-` in
your own new bullet if unsure, matching the immediately-preceding bullet's
literal separator character exactly.)

### 6. `Docs/flows/spawn-event-to-entity.md`
In `## Sequence`, change the existing line:
```markdown
3. Expansion owns count, spread, jitter, bounds, deterministic id, and command
   production.
```
to:
```markdown
3. Expansion owns count, spread, jitter, bounds, deterministic id, launch-aim
   acquisition, and command production.
```
No other change to this file.

## Relevant Global Context
- All claims in the inserted text are already true of the shipped
  implementation (tasks 001-004) and already covered by tests (task 005). This
  is a pure documentation-accuracy task; do not soften, hedge, or add TODO/future
  language — the feature is complete, not planned.
- Cross-reference links use relative Markdown paths consistent with each
  target file's existing link style (`../` segments) — check each file's
  existing links to sibling docs for the exact relative-path convention before
  adding new ones (e.g. `spawn-events-and-commands.md` already links to
  `../reference/simulation/spawn-template-registry.md`, confirming the
  `Docs/contracts/` → `Docs/reference/simulation/` relative path).

## Dependencies Confirmed
- Tasks 001-005 complete and verified (see `implementation-log.md`): the
  behavior described in every snippet above matches the actual shipped code
  read directly during this plan's execution.

## Step-By-Step Instructions
1. For each of the six files, re-read it to find the exact anchor text quoted
   above (it may have shifted slightly if unrelated doc edits landed since
   this plan started — search for the quoted phrase, don't assume line
   numbers).
2. Insert the corresponding block exactly as given, adjusting only whitespace
   to match the file's existing conventions (blank line before/after new
   sections, list-item indentation, etc.).
3. Do not reformat or touch any other part of these files.
4. After editing, grep each file for the new anchor phrases
   ("Projectile Launch Aim", "Launch Aim", "LaunchAimMode") to confirm each
   insertion landed exactly once and in the right file.

## Acceptance Criteria
- Docs state authored source of truth is trigger link.
- Docs state root/player casts use manual aim or existing aim assist only.
- Docs state both discrete and continuous triggered projectiles may launch-aim.
- Docs distinguish one-time launch aim from discrete-only homing.
- Docs state successful acquisition redirects exactly one existing
  deterministic shot per event/wave; all remaining shots retain normal
  nova/pattern velocities.
- Docs state aimed shot has no spread/jitter, wave count is unchanged, and
  normal pattern RNG consumption remains stable for non-aimed shots.
- Docs state contact-gated source target is excluded.
- Docs state target spatial hash and common acquisition helper are reused with
  proper build/consumer synchronization.
- Docs state no entity component/archetype/pool changes.

## Validation Required
- Static: grep each modified file for the new section headings/bullets to
  confirm they landed exactly once, in the right file, at the right location.
- Read back each modified file's changed region once after editing to confirm
  Markdown renders sensibly (matched code fences, correct list nesting,
  correct relative links).
- No code compiles here — there is nothing to build/test. State this plainly
  in your report rather than describing "test execution."

## Hard Boundaries
- Do not modify any file under `Assets/` (code or tests).
- Do not modify any `Docs/` file not in the allowed list.
- Do not add new documentation files.
- Do not reopen index-level decisions or restate them differently than the
  shipped behavior.
- Do not hand-edit Unity `.meta` files (not applicable here, but stated for
  consistency with the rest of this plan).

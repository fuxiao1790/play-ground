# Task Execution Packet

## Task
009-update-aim-oriented-nova-docs.md

## Goal
Task 006 documented the now-superseded "shot index 0 only" launch-aim behavior
across five design-reference docs. Task 007 changed the actual behavior to a full
aim-oriented radial nova (every shot recomputed from the acquired direction, not
just one). Correct the docs to match. This is a documentation-only task: no code or
test changes.

Two of the five listed docs contain a demonstrably false claim (confirmed by
direct read at the start of this task) and MUST be corrected with the exact text
given below. One doc's launch-aim section is high-level enough that it benefits
from stating the nova formula explicitly (also given below). The remaining two
listed docs were checked and contain NO stale "shot 0 only" or "RNG-preserved"
claims — grep for "shot" in both turned up only unrelated "Snapshot" substring
matches. Do not force an edit into those two files if, on your own re-read, you
confirm the same — a documentation task should not add padding to files that are
already accurate. If you find something stale in them that this packet missed,
correct it and note the discrepancy in your report.

## Files Allowed To Modify
- Docs/reference/game-logic/skill-gameplay-system.md
- Docs/contracts/spawn-events-and-commands.md
- Docs/reference/simulation/projectile-system.md
- Docs/reference/simulation/spawn-template-registry.md (verify only — see above)
- Docs/flows/spawn-event-to-entity.md (verify only — see above)

## Files Allowed To Create
- None.

## Files Likely Needed For Reading
- All five files above, in full (re-read each to confirm current exact text before
  editing — other changes may have landed since this packet was written).
- Assets/Scripts/System/Projectiles/ProjectileSpawnExpansionSystem.cs (source of
  truth for `CreateAimedNovaPattern`/`TryResolveAimedDirection` — confirms the
  formula before you write it into docs; already the basis for task 007's/008's
  verified worked examples).

## Behavior To Preserve
- Do not touch any file not in the allowed list.
- Do not change any content beyond what's described below — no reformatting, no
  unrelated cleanup.
- Match each file's existing heading level, list style, and voice (terse,
  factual, code-referencing).
- The feature is complete and tested (tasks 001-008); write in present tense as
  shipped behavior, not as a plan or TODO.

## Behavior To Change (Exact Replacements)

### 1. `Docs/reference/simulation/projectile-system.md`
In the `## Launch Aim` section (added by task 006), replace the second and third
paragraphs — the ones starting "When a wave's template carries..." and "Both the
discrete and continuous lanes receive the same one-shot replacement..." — leave
the FIRST paragraph ("Trigger-authored launch aim gives one triggered projectile
wave a one-time initial-velocity redirect...") untouched, since it already speaks
of the whole wave and is still accurate.

Replace this paragraph (currently reading, in part, "...On success it replaces
only deterministic shot index 0's velocity with the exact normalized direction to
the target times the template's `Speed`; every other shot in the wave keeps its
normal forward/side-spray/radial pattern velocity, and the full pattern math and
RNG draws still run for shot 0 before its velocity is overwritten, so later
shots' RNG sequence is unaffected by whether launch aim is enabled...") with:

```markdown
When a wave's template carries `LaunchAimMode == NearestHostile`, `Range > 0`,
and a valid faction, expansion queries the shared `TargetSpatialHashSingleton`
through `CombatTargetAcquisition.TrySelectNthNearest` (the same nearest-hostile
selection targeted chains use, rank 0, excluding the event's contact-gate seed
target) once per event. On success it recomputes every shot in the wave as one
radial nova oriented from the acquired direction: shot `i`'s velocity is
`Rotate(aimDirection, 360 degrees * i / count) * Speed`, so shot `0` points
exactly at the target and shots `1..count-1` occupy the remaining equally
`360/count`-spaced slots around it. This bypasses the stored
forward/side-spray/radial pattern and its spread/jitter/RNG entirely for that
wave — a successful aim is a nova, not a partial pattern edit. A missing hash
singleton, non-positive range, `CombatFaction.None`, no hostile in range, or a
target coincident with the spawn position all fall through to the unmodified
stored pattern output.
```

Replace the paragraph currently reading "Both the discrete and continuous lanes
receive the same one-shot replacement, since acquisition runs once per event
before `WriteCommand` routes each shot by `ContinuousCollision`..." with:

```markdown
Both the discrete and continuous lanes receive the same aimed nova, since
acquisition runs once per event before `WriteCommand` routes each shot by
`ContinuousCollision`. Launch aim only sets each shot's initial velocity; it
adds no per-frame steering, target ownership, or entity state, and it does not
enable tracking — the continuous archetype still has no
`ProjectileTrackingComponent`, and a discrete projectile's separately authored
tracking (see [Tracking And Movement](#tracking-and-movement)) may still steer
after an aimed launch. `ProjectileSpawnExpansionSystem` schedules its
acquisition read dependent on `TargetSpatialHashSingleton.BuildHandle` and
publishes its handle into `ConsumerHandle`, exactly like `TargetedResolveSystem`.
```

No change to `## Authoring Notes`'s launch-aim bullet (added by task 006) — it
does not mention shot count or nova shape, so it remains accurate as-is; verify
this on read and leave it untouched.

### 2. `Docs/contracts/spawn-events-and-commands.md`
Replace the sentence "Expansion resolves nearest-hostile acquisition once per
event/wave from the dereferenced template, before count/spread fan-out and before
the discrete/continuous split, and redirects only one deterministic shot's
velocity." (the second sentence of the paragraph added by task 006, immediately
before its trailing "See [Projectile System](...)" link) with:

```markdown
Expansion resolves nearest-hostile acquisition once per event/wave from the
dereferenced template, before count/spread fan-out and before the
discrete/continuous split; on success it recomputes the whole wave as one
radial nova oriented from the acquired direction (shot `0` points at the
target, remaining shots are spaced `360/count` degrees around it), bypassing
the stored pattern entirely — it does not redirect a single shot in isolation.
```

Keep the paragraph's first sentence ("`ProjectileSpawnCommand` also carries
authored launch-aim policy...") and trailing link sentence exactly as they are —
only the middle sentence changes.

### 3. `Docs/reference/game-logic/skill-gameplay-system.md`
In the `### Projectile Launch Aim` subsection (added by task 006), replace the
second paragraph (currently: "Launch aim is independent of discrete-only homing
(`Tracking`/`trackingEnabled` on `ProjectileDefinition`): a triggered projectile
may enable one, the other, both, or neither. Launch aim is a one-time
initial-velocity redirect applied at spawn; homing is continuous re-steering
after spawn, and only the discrete projectile archetype carries a tracking
component at all. See [Projectile System](../simulation/projectile-system.md#launch-aim)
for the runtime acquisition/redirect mechanics.") with:

```markdown
Launch aim is independent of discrete-only homing (`Tracking`/`trackingEnabled`
on `ProjectileDefinition`): a triggered projectile may enable one, the other,
both, or neither. On successful acquisition, launch aim recomputes the whole
wave as one full radial nova oriented from the acquired direction — shot `0`
points exactly at the target, and every other shot is spaced evenly (`360`
degrees divided by shot count) around it — applied once at spawn, not steered
per frame; homing, by contrast, is continuous re-steering after spawn, and only
the discrete projectile archetype carries a tracking component at all. Failed
or disabled acquisition leaves the trigger's normally authored pattern
untouched. See [Projectile System](../simulation/projectile-system.md#launch-aim)
for the exact rotation formula and fallback conditions.
```

Do not change the first paragraph of this subsection ("A trigger link may
additionally author a projectile launch-aim policy...") — it is unaffected by the
nova-shape change.

### 4. `Docs/reference/simulation/spawn-template-registry.md` (verify only)
Re-read the `LaunchAimMode` / `LaunchAimRange` bullet in `## Projectile Runtime
Snapshot` (added by task 006). It currently describes the fields as template-level
policy that participates in `SpawnTemplateHash` — it does not claim single-shot or
RNG-preservation semantics. If, on your own re-read, it is still accurate, make no
edit to this file and say so in your report. If you find it implies "only one
shot changes" or similar, correct it to match the nova behavior described in
`projectile-system.md`'s `## Launch Aim` section.

### 5. `Docs/flows/spawn-event-to-entity.md` (verify only)
Re-read sequence step 3 (added by task 006): "Expansion owns count, spread,
jitter, bounds, deterministic id, launch-aim acquisition, and command
production." This is generic and does not claim single-shot semantics. If, on
your own re-read, it is still accurate, make no edit to this file and say so in
your report.

## Relevant Global Context
- All claims in the replacement text above are already true of the shipped
  implementation (task 007) and already covered by tests (task 008). Write in
  present tense as shipped behavior.
- Cross-reference links use relative Markdown paths consistent with each target
  file's existing link style — do not change any existing link's path, only the
  surrounding prose.

## Dependencies Confirmed
- Tasks 007 and 008 complete (see `implementation-log.md`): the nova behavior
  described in every replacement snippet above matches
  `ProjectileSpawnExpansionSystem.CreateAimedNovaPattern`/
  `TryResolveAimedDirection`, confirmed by direct file read, and is covered by the
  two new tests added in task 008.

## Step-By-Step Instructions
1. Re-read all five files to confirm current exact text at each anchor point
   (search for the quoted phrases above — don't assume line numbers, since task
   006's insertions may have shifted).
2. Apply the three exact replacements (files 1-3 above).
3. Verify files 4 and 5 per their "verify only" instructions; edit only if you
   find an actual stale claim, and clearly note in your report which of the two
   you did or didn't change and why.
4. Do not reformat or touch any other part of these five files.
5. After editing, grep each changed file for "shot" and "RNG" to confirm no
   remaining claim that only one shot changes or that RNG sequence is preserved
   across the aim-enabled/disabled boundary.

## Acceptance Criteria
- Docs define successful launch aim as full radial nova oriented from acquired
  direction.
- Formula and deterministic slot `0` direct-target behavior stated.
- Docs state remaining shots use equal `360/count` offsets.
- Docs state successful aimed nova bypasses stored forward/side-spray pattern and
  spread/jitter; failed/disabled acquisition preserves them.
- Both lanes, trigger-only authoring, manual root aim exclusion, homing
  independence, target-hash reuse, and unchanged archetypes remain documented
  (already true from task 006's insertions — do not remove this coverage while
  editing).
- No stale statement says only shot `0` changes or later-shot RNG matches
  disabled policy.

## Validation Required
- Static: grep the three definitely-changed files for the new nova-formula
  phrasing to confirm each landed exactly once.
- Grep all five files for "shot" and "RNG"/"random" to confirm no surviving
  stale claim anywhere in this plan's doc footprint.
- Read back each changed region once after editing to confirm Markdown renders
  sensibly (matched code fences, correct list nesting, correct relative links).
- No code compiles here — state plainly that there is nothing to build/test,
  rather than describing test execution.

## Hard Boundaries
- Do not modify any file under `Assets/` (code or tests).
- Do not modify any `Docs/` file not in the allowed list.
- Do not modify `Docs/random-ideas.md` or any other personal/unrelated doc — a
  prior task in this plan accidentally touched an out-of-scope doc and it had to
  be reverted; do not repeat that mistake.
- Do not add new documentation files.
- Do not reopen index-level decisions or restate them differently than the
  shipped behavior.
- Do not hand-edit Unity `.meta` files.

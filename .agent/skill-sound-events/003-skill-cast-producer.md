# 003 — Skill cast sound: authoring → clip id → enqueue

The first and, for now, only producer.

## Why

Skill cast is the sound the player most needs to hear and the one this work was
scoped to. It is also **not** low-count: `MobRoot.cs:505` calls
`skillDriver.Tick(true, ...)`, and `SkillDriver.Tick` fires every ready slot every
frame while `fireHeld` (`SkillDriver.cs:100-129`), so the rate is
`mobCount × slots / recovery`.

## Change

### Authoring

Add a cast clip to the `Skill` ScriptableObject (`Assets/Scripts/Skills/Skill.cs`),
so every skill kind (`ProjectileSkill`, `AoeSkill`, `LingeringAoeSkill`,
`TargetedSkill`) inherits it:

```csharp
[SerializeField] private AudioClip castSound;
[SerializeField, Min(0f)] private float castSoundRadius;   // 0 = project default
public AudioClip CastSound => castSound;
public float CastSoundRadius => castSoundRadius;
```

A loud detonation and a dagger flick must not share one cull radius — one global
number makes one of them wrong ([002](./002-drain-and-selection.md)).

### Runtime definition

Add to `RuntimeSkillDefinition`
(`Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`), directly beside
`RenderId` (`:10`) and following its convention:

```csharp
// Sound identity from the AudioRoot clip id space; 0 means "no sound".
public int CastSoundId { get; set; }

// Spatial cull radius for the cast sound; 0 means "use the AudioRoot default".
public float CastSoundRadius { get; set; }
```

### Resolution

Resolve the id in `SkillDriver.CompileAndRegister` (`SkillDriver.cs:196`), in the
same pass as the existing `RegisterProjectileTypes` / `RegisterAoeTypes` /
`RegisterTargetedTypes` / `RegisterSpawnTemplates` calls:

```csharp
def.CastSoundId = audioRoot != null ? audioRoot.Register(skill.CastSound) : 0;
```

Registration must happen here, not at cast time.
`Docs/architecture/layer-rules.md` §*Runtime, Authoring, And Configuration*
requires runtime data to be copied from authoring data before simulation, and
`coding-standards.md` §*Update Timing* forbids hiding setup inside repeated
runtime calls. `Register` dedupes ([001](./001-audio-root.md)), so re-running on
every loadout edit is free.

Only root slots need a cast id — cast sound fires at the cast site, not for
nested spawn children. Do not walk the `Register*Recursive` trees for this.

### Emission

In `SkillDriver.Tick`, inside the existing `SpawnMarker` block
(`SkillDriver.cs:112-125`), next to `SkillSpawnTranslator.Spawn`:

```csharp
if (compiledSlots[i].CastSoundId > 0 && audioRoot != null)
{
    audioRoot.Enqueue(new SoundEvent
    {
        ClipId = compiledSlots[i].CastSoundId,
        Position = new float2(transform.position.x, transform.position.y),
        Velocity = default,           // see below
        AudibleRadius = compiledSlots[i].CastSoundRadius,
        Category = SoundCategory.Cast,
        Priority = castPriority
    });
}
```

Guard with `> 0` exactly as `VfxEmit.cs:15` guards `vfxId`.

`AudibleRadius` comes from a `[SerializeField] private float castSoundRadius;`
on `Skill`, compiled into `RuntimeSkillDefinition.CastSoundRadius` in the same
pass as `CastSoundId`. `0` is a valid authored value meaning "use the project
default" ([002](./002-drain-and-selection.md)), so no migration is needed for
skills authored before the field existed.

`Velocity` is written as `default`. The caster's velocity is available
(`PlayerRoot`/`MobRoot` both own movement), but nothing reads the field until a
spatializer exists, and a cast is emitted at a point rather than carried by a
moving body. Populating it is a one-line change at that time; guessing at its
units now is not. See [index.md](./index.md) Decision 6.

The sound is emitted **only on an actual fire** — after the `SpawnBlocked` check
(`SkillDriver.cs:106`) and inside the branch that reaches `ResetOnFire()`
(`:127`). A blocked or refunded cast (`RefundFire`, `:108` and `:172`) must not
sound.

Note `ReceiveSpawnRejected` (`:158`) refunds a cast *after* the frame it fired
in, so its sound has already played. That is accepted: rejection is rare, and
suppressing it would mean deferring every cast sound by a frame. Record the
choice in the contract doc ([004](./004-docs.md)).

### Priority

Player casts must outrank mob casts, otherwise a mob crowd drowns the player's
own feedback. `SkillDriver` already carries `CombatFaction faction`
(`SkillDriver.cs:34`) — derive priority from it. Do not add a serialized field
for this.

### Wiring fix

`SkillDriver.cs:72` resolved `AudioManager.Instance` in `Awake`, racing the
audio root's own `Awake` — Unity does not order `Awake` between MonoBehaviours.
`coding-standards.md` §*Awake vs OnEnable Boundary* puts cross-MonoBehaviour work
in `OnEnable`/`Start`. [001](./001-audio-root.md) deleted that line; this task
adds the correct version:

- Resolve `audioRoot` in `OnEnable`, falling back to `AudioRoot.Instance` if the
  serialized field is unset.
- Do **not** restore the `FindAnyObjectByType` scan.
  `coding-standards.md` §*Unity Object Access* prefers serialized references and
  §*Fail Fast Validation* prefers a clear setup error over a scene scan.
- Validate once at setup and log a single clear error if unresolved. This is the
  **wiring** half of the fail-fast split in [index.md](./index.md) §*Open
  Questions*; individual dropped events still only increment a counter.

An unresolved `audioRoot` must leave the game fully playable and silent, never
throwing from `Tick`.

## Acceptance Criteria

- A `Skill` with no `castSound` produces `CastSoundId == 0` and enqueues nothing.
- Clip ids survive a loadout edit — `CompileAndRegister` re-runs and the same
  clip yields the same id.
- No sound is enqueued for a `SpawnBlocked` slot or a same-frame refunded cast.
- Player-faction casts carry higher priority than mob-faction casts.
- The `audioRoot` field on `SkillDriver` is finally used — the dead wiring noted
  in [index.md](./index.md) is resolved, not left in place.
- A missing `AudioRoot` logs one setup error and produces silence, not an
  exception per cast.
- Holding fire with several slots produces thinned but continuous cast sound,
  not silence and not a machine-gun.

## Tests

`Assets/Tests/EditMode/ProjectileContinuousAuthoringEditModeTests.cs:83` already
drives `driver.Tick(true, ...)` with no audio root present — it must keep
passing, which is the regression guard for "unresolved root is silent, not
fatal".

Add `Assets/Tests/EditMode/SkillCastSoundEditModeTests.cs` for cast-id
resolution: compile a loadout whose skill has a clip, assert `CastSoundId > 0`
and that it is stable across a recompile, and that `castSoundRadius` reaches
`RuntimeSkillDefinition.CastSoundRadius`.

## Dependencies

[002](./002-drain-and-selection.md).

## Scope

Medium — one authoring field, one runtime field, one registration line, one
emission block, plus the `OnEnable` wiring.

## Editor Work (User)

Assign the `AudioRoot` reference on each `SkillDriver` (player and mob prefabs),
and assign a cast `AudioClip` on each `Skill` asset you want audible. Skills with
no clip stay silent by design, so nothing breaks if you only assign a few. Make
sure an `AudioRoot` exists in the scene under test.

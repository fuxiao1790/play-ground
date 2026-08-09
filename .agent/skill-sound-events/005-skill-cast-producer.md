# 005 — Skill cast sound: authoring → clip id → enqueue

The first and, for now, only producer.

## Why

Skill cast is the sound the player most needs to hear and the one this work was
scoped to. It is also **not** low-count: `MobRoot.cs:505` calls
`skillDriver.Tick(true, ...)`, and `SkillDriver.Tick` fires every ready slot
every frame while `fireHeld` (`SkillDriver.cs:96-125`), so the rate is
`mobCount × slots / recovery`.

## Change

### Authoring

Add a cast clip to the `Skill` ScriptableObject
(`Assets/Scripts/Skills/Skill/`), beside the existing `BaseRate` that
`SkillSetCompiler.cs:57` already reads:

```csharp
[SerializeField] private AudioClip castSound;
public AudioClip CastSound => castSound;
```

### Runtime definition

Add to `RuntimeSkillDefinition` (`Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`),
directly beside `RenderId` and following its documented convention:

```csharp
// Sound identity from the AudioManager clip id space; 0 means "no sound".
public int CastSoundId { get; set; }
```

### Resolution

Resolve the id in `SkillDriver.CompileAndRegister` (`SkillDriver.cs:191`),
in the same pass as the existing `RegisterProjectileTypes` /
`RegisterAoeTypes` / `RegisterSpawnTemplates` calls:

```csharp
def.CastSoundId = audioManager != null ? audioManager.Register(skill.CastSound) : 0;
```

Registration must happen here, not at cast time —
`Docs/architecture/layer-rules.md` §*Runtime, Authoring, And Configuration*
requires runtime data to be copied from authoring data before simulation, and
`coding-standards.md` §*Update Timing* forbids hiding setup inside repeated
runtime calls. `Register` dedupes (task 002), so re-running on every loadout
edit is free.

### Emission

In `SkillDriver.Tick`, inside the existing `SpawnMarker` block
(`SkillDriver.cs:108-121`), next to `SkillSpawnTranslator.Spawn`:

```csharp
if (compiledSlots[i].CastSoundId > 0)
{
    audioManager.Enqueue(new SoundEvent
    {
        ClipId = compiledSlots[i].CastSoundId,
        Position = new float2(transform.position.x, transform.position.y),
        Category = SoundCategory.Cast,
        Priority = /* player caster above mob caster */
    });
}
```

Guard with `> 0` exactly as `VfxEmit.cs:15` guards `vfxId`.

The sound is emitted **only on an actual fire** — after the `SpawnBlocked`
check and inside the branch that calls `ResetOnFire()`. A blocked or refunded
cast (`RefundFire`, `SkillDriver.cs:104` and `:167`) must not sound.

### Priority

Player casts must outrank mob casts, otherwise a mob crowd drowns the player's
own feedback. `SkillDriver` already carries `CombatFaction faction`
(`SkillDriver.cs:34`) — derive priority from it. Do not add a new serialized
field for this.

## Fix while here

`SkillDriver.cs:71` resolves `AudioManager.Instance` in `Awake`, which races
`AudioManager.Awake` (`AudioManager.cs:38`) — Unity does not order `Awake`
between MonoBehaviours. `coding-standards.md` §*Awake vs OnEnable Boundary*
states cross-MonoBehaviour work belongs in `OnEnable`/`Start`. Move the
resolution to `OnEnable`.

Also drop the `FindAnyObjectByType<AudioManager>()` fallback: `coding-standards.md`
§*Unity Object Access* prefers serialized references, and §*Fail Fast Validation*
prefers a clear setup error over a scene scan. Validate the reference once at
setup instead — this is the "wiring" half of the fail-fast split recorded in
[index.md](./index.md) §*Open Questions*. Individual dropped events still just
increment a counter.

## Acceptance Criteria

- A `Skill` with no `castSound` produces `CastSoundId == 0` and enqueues nothing.
- Clip ids survive a loadout edit — `CompileAndRegister` re-runs and the same
  clip yields the same id.
- No sound is enqueued for a `SpawnBlocked` slot or a refunded cast.
- Player-faction casts carry higher priority than mob-faction casts.
- `SkillDriver.cs:32` `audioManager` field is finally used — the dead wiring
  noted in [index.md](./index.md) is resolved, not left in place.
- No `AudioClip` reference reaches ECS or any job; only `CastSoundId` crosses.
- Holding fire with several slots produces thinned but continuous cast sound,
  not silence and not a machine-gun.

## Dependencies

[004](./004-sound-event-bridge.md).

## Scope

Medium — one authoring field, one runtime field, one registration line, one
emission block, plus the `Awake`/`OnEnable` fix.

## Editor Work (User)

Assign a cast `AudioClip` on each `Skill` asset you want audible, and make sure
an `AudioManager` exists in the scene under test — it is currently only in
`Assets/Scenes/BenchmarkLarge.unity`. Skills with no clip stay silent by design,
so nothing breaks if you only assign a few.

# 001 — Delete `AudioManager`; add `AudioRoot`

## Why

`AudioManager` has zero callers, so there is nothing to preserve and no
migration to perform. It is not a constraint on this design — it is a file to
delete. See [index.md](./index.md) Decision 1.

`AudioRoot` is written into `PlayGround.Sim` because Sim is the only assembly
callable from both GameLogic (`SkillDriver`, today's producer) and any future ECS
presentation code — `Ui -> GameLogic -> Sim` is one-way
(`Docs/architecture/layer-rules.md` §*Package Boundary*). `CombatVfxRoot` is the
precedent: a `MonoBehaviour` inside Sim, referenced downward by `SkillDriver`.

This task delivers the vessel — data contract, registry, voice pool, `Enqueue`.
The drain and the ranking are [002](./002-drain-and-selection.md).

## Change

### Delete

- `Assets/Scripts/Audio/AudioManager.cs` and its `.meta`.
- The now-empty `Assets/Scripts/Audio/` folder and its `.meta`.

Do **not** attempt to preserve the GUID. Scene cleanup is user editor work
(below).

### New: `Assets/Scripts/System/Audio/SoundEvent.cs`

Namespace `PlayGround.System.Combat.Audio`, matching the sibling
`PlayGround.System.Combat.Vfx`.

```csharp
public enum SoundCategory : byte
{
    None = 0,
    Cast,
    Hit,
    Spawn,
    Death,
    Ui
}

public struct SoundEvent
{
    public int ClipId;            // 0 = none
    public float2 Position;       // world position of the emitter at emit time
    public float2 Velocity;       // emitter velocity; zero for a static emitter
    public float AudibleRadius;   // 0 = fall back to AudioRoot.defaultAudibleRadius
    public SoundCategory Category;
    public short Priority;        // higher wins; ties broken by novelty in 002
}

// Mirrors AoeVfxIds (AoeVfxEcsComponents.cs:19-26). One field today; the
// remaining slots land beside their VFX siblings when their producers do.
public struct SkillSoundIds
{
    public int SpawnId;
}
```

`SkillSoundIds` is a plain struct, not `IComponentData`. `AoeVfxIds` carries that
interface because ECS AOE entities hold it and Burst emits from it; no ECS code
touches sound ids yet, so claiming the role now would be the same speculative
machinery [index.md](./index.md) Decision 3 defers. The upgrade is adding one
interface when the lane lands.

Keep `SoundEvent` fully unmanaged — no reference field, no `AudioClip`. Nothing
requires it today; it is a data-shape choice with zero added code that lets a
future Burst lane carry the struct unchanged
([index.md](./index.md) Decision 3).

`SoundCategory` values beyond `Cast` have no producer yet. They are declared now
because the enum is the extension point, and adding a value later is a data
contract change while adding a producer is not.

### What earns a payload slot

The payload is sized for a future localization pass so that pass reads the same
struct rather than redefining it ([index.md](./index.md) Decision 6). The
admission rule:

> **A field belongs in `SoundEvent` if it describes what happened in the world.
> It belongs on `AudioRoot` if it describes how audio should respond.**

| Field | Kind | Why |
|---|---|---|
| `ClipId` | Occurrence | Which sound happened. |
| `Position` | Occurrence | Where it happened. Cull input and `AudioSource` position. |
| `Velocity` | Occurrence | How the emitter was moving. Doppler input for a future spatializer; Unity derives Doppler from `AudioSource` transform motion, which a fire-and-forget one-shot has none of. Producers write `default` today. |
| `AudibleRadius` | Occurrence | How far this emitter carries. Authored per-skill — a detonation and a dagger flick do not reach equally. Consumed alongside `Position` in one cull test, and later the same number is the `maxDistance` rolloff. |
| `Category` | Occurrence | What kind of thing happened. |
| `Priority` | Occurrence | How much it mattered to the emitter. |

Deliberately **not** in the payload — all of it is response policy, so all of it
is serialized configuration on `AudioRoot`:

- **Pitch jitter, repeat start delay, repeat volume falloff.** These are how the
  manager decorrelates duplicates it chose to play. An event has no opinion about
  them, and a producer that could set them would be reaching across into playback.
- **`maxCopiesPerClipPerFrame`, `maxActiveSources`, `sameSoundStartSpacingSeconds`.**
  Budget, not world state.
- **Per-sound base volume.** A property of the *clip*, not the occurrence.
  Belongs beside the clip in the registry, authored once instead of copied into
  every event.
- **An emitter handle to follow** (`Transform`, `Entity`, or emitter id). A
  sound that tracks a moving emitter is not fire-and-forget: the pool must
  re-position that voice every frame and release it when the emitter dies. That
  is a lifetime model, not a payload field. Cast sounds fire at a point and are
  done. If follow-sounds are wanted later, they arrive as a second play mode with
  their own task.

### New: `Assets/Scripts/System/Audio/AudioRoot.cs`

```csharp
public sealed class AudioRoot : MonoBehaviour
```

Serialized configuration:

| Field | Default | Purpose |
|---|---|---|
| `maxActiveSources` | `24` | voice pool cap |
| `prewarmSources` | `8` | sources created at setup |
| `sameSoundStartSpacingSeconds` | `0.04` | cross-frame backstop |
| `defaultAudibleRadius` | — | fallback when `SoundEvent.AudibleRadius <= 0` |
| `maxCopiesPerClipPerFrame` | `3` | per-clip cap, applies regardless of free voices |
| `repeatVolumeFalloff` | `0.8` | used by 002 |
| `pitchJitterRange` | `0.06` | ≈ ±1 semitone, applied to every play |
| `repeatStartDelayMaxSeconds` | `0.02` | applied to copy index > 0 only |
| `listenerObject` (`GameObject`) | none | its transform is the listener position, used by 002 |
| `spatialBlend` | `0.7` | **not `0`** — see below |
| `rolloffMode`, `minDistance`, `maxDistance` | — | applied to pooled `AudioSource`s |

`spatialBlend` defaulted to `0` in the deleted `AudioManager`, meaning every
sound played fully 2D, dead center, at full level regardless of position. With
spatial culling that produces a hard on/off cliff at the radius and no falloff
inside it. Default it to `0.7` so position actually reaches the mix — otherwise
`Position` and `AudibleRadius` are carried and then ignored.

Static `Instance`, set in `Awake`, cleared in `OnDestroy` — same shape as
`CombatVfxRoot.cs:13`, `:26-50`, including the "a root already exists" error path
that disables the duplicate rather than silently overwriting.

Public surface this task delivers:

```csharp
// Returns 0 for null. Stable across repeated registration of the same clip.
public int Register(AudioClip clip);

// False for id <= 0 or out of range.
public bool TryGetClip(int clipId, out AudioClip clip);

// Main thread only. Appends to the pending batch; plays nothing.
public void Enqueue(in SoundEvent soundEvent);

// Rebind the listener at runtime — respawn, camera swap, cutscene.
public void BindListener(GameObject listenerObject);

public void Clear();
```

`BindListener` follows the existing rebind idiom on `SkillDriver`
(`BindCombatRoot` `:133`, `BindVfxRoot` `:144`, `BindCaster` `:153`) rather than
inventing a new one. It matters because the listener is likely the player, and
the player object is destroyed on death and recreated on respawn.

**Listener handling:**

- Serialize a `GameObject`, not a `Transform` or a `Camera`. The listener is
  whatever object the game says it is — player, camera, a dedicated marker — and
  `AudioRoot` has no business caring which.
- Cache its `Transform` in `OnEnable` and in `BindListener`, so the drain does
  one field read rather than a `.transform` lookup.
- **No `Camera.main` fallback.** `coding-standards.md` §*Unity Object Access*
  prefers serialized references and §*Fail Fast Validation* bans hiding bad setup
  behind convenience resolution. Unassigned is a setup error, not a default.
- Re-check the cached transform for null in the drain. A destroyed listener is a
  normal runtime state here, not a bug, so it must not throw and must not spam —
  see [002](./002-drain-and-selection.md).

**Registry**, modelled on `CombatVfxRoot.cs:23`/`:52-110`:

```csharp
private readonly Dictionary<AudioClip, int> idsByClip = new();
private readonly List<AudioClip> clipsById = new();   // index 0 unused
```

- **Id `0` means "none"**, matching `CombatVfxRoot.Register` returning `0` for a
  null asset. Producers guard with `if (clipId <= 0) return;` as `VfxEmit.cs:15`
  and `VfxEmit.cs:37` do.
- **Dedupe by clip reference.** `Register` returns the existing id when the clip
  is already known. This is what keeps ids stable across
  `SkillDriver.CompileAndRegister`, which re-runs on every loadout edit
  (`SkillDriver.cs:196`) — the same reason `CombatVfxRoot` keeps `idsByAsset`.
- Ids are dense and start at `1`.

**Voice pool**, re-deriving the logic that was in `AudioManager` (correct code,
new home): prewarm at setup, grow lazily to `maxActiveSources`, prune finished
voices, track per-clip playing counts and last-start times. `Docs/coding-standards.md`
§*Update Timing* forbids hiding pool creation in repeated runtime calls, so
prewarm happens in `Awake`.

Deliberate departures from the deleted code:

- **`source.volume` is always set explicitly before playing.** The old code never
  set it (`AudioManager.cs:91-95`), so a pooled source inherited whatever the
  previous sound left behind. 002 needs volume as an output of ranking; this is
  also a latent bug fix.
- **`source.pitch` is jittered on every play**, `1 ± pitchJitterRange`. Copies of
  one clip started on the same frame are sample-aligned and sum coherently — two
  copies are `+6 dB`, four are `+12 dB`, and it reads as one loud sound rather
  than several. Pitch jitter decorrelates them.
- **Copies after the first are delayed**, `PlayDelayed(rand(0, repeatStartDelayMaxSeconds))`
  for copy index > 0. Spreading the onsets is what actually separates transients.
  **Copy index 0 always plays immediately** — delaying the player's own first
  cast by up to 20 ms is felt.
- **Voice lifetime accounts for pitch and delay:**
  `EndTime = now + delay + clip.length / pitch`. The old `now + clip.length`
  (`AudioManager.cs:96`) was safe only because pitch was never touched; at pitch
  `0.94` the clip runs *longer* than `clip.length` and the old bookkeeping prunes
  it early, handing a still-sounding source back to the pool. `PlayDelayed` sets
  `isPlaying` immediately, so the `!isPlaying` free-source check stays correct —
  stated here so it is not "fixed" later.
- **Jitter uses a seeded `Unity.Mathematics.Random` field on `AudioRoot`**, not
  `UnityEngine.Random` — audio must not perturb global random state that gameplay
  draws from. Matches the existing seeded-jitter idiom (`TimedSpawnComponent.JitterSeed`,
  `SkillDriver.cs:1017`).
- **Same-clip spacing is evaluated against a snapshot** taken at the start of the
  drain, with new start times written only after the batch. Every event in one
  drain shares the same `Time.time`, so checking-and-updating per play would make
  the second copy's `now - lastStart` equal `0` and cull every in-frame duplicate
  — silently disabling the ranking in 002. Spacing is a cross-frame tool only.
- **No public `PlaySound(clip, ...)`.** Playback is internal to the drain. A
  public per-call entry point would reintroduce the greedy allocator that
  [index.md](./index.md) Decision 1 removes.

**Pending batch:**

```csharp
private readonly List<SoundEvent> pending = new();
```

Reused across frames, never reallocated (`coding-standards.md` §*Allocation
Rule*). `Enqueue` appends and returns; it makes no decision.

`Clear()` stops all voices and empties `pending`, but must **not** drop
registrations — clip ids are authored identity, not playback state. Registration
lifetime is the `AudioRoot` instance.

### Update `SkillDriver`

`SkillDriver.cs:3` (`using PlayGround.Audio;`), `:32` (the serialized
`AudioManager` field), and `:72` (the `Awake` resolution) reference a type that
no longer exists. This task only needs the project to compile; the correct wiring
is [003](./003-skill-cast-producer.md).

Retype the field to `AudioRoot` and delete the `Awake` line outright — do not
port `FindAnyObjectByType` forward. 003 adds the `OnEnable` resolution and the
setup validation.

## Acceptance Criteria

- No file under `Assets/Scripts/` references `PlayGround.Audio` or
  `AudioManager`.
- `PlayGround.Sim.asmdef` is **unchanged** — no PlayGround reference added. This
  is the single most important check in the task.
- `Register(null)` returns `0` without allocating an entry.
- `Register(clip)` twice returns the same id.
- `TryGetClip` round-trips every registered id; returns `false` for `0`,
  negatives, and out-of-range ids.
- `Clear()` leaves registrations intact.
- A second `AudioRoot` in the scene logs an error and disables itself rather than
  replacing `Instance`.
- `Enqueue` plays nothing and allocates nothing after warmup.
- The project compiles with `SkillDriver` still passing no sound anywhere.

## Tests

EditMode, no scene needed. Add
`Assets/Tests/EditMode/AudioClipRegistryEditModeTests.cs` covering the registry
criteria above. This is the first audio test in the project.

`AudioRoot` is a `MonoBehaviour`; instantiate with
`new GameObject().AddComponent<AudioRoot>()` and destroy in teardown. Test
through the public API — do not add a test-only accessor
(`coding-standards.md` §*Test Hooks*).

## Dependencies

None. Must land before every other task.

## Scope

Medium — one deletion, two new files, one field retype. Most of the code is
re-derived pool logic that already exists and is known correct.

## Editor Work (User)

After this lands, `Assets/Scenes/BenchmarkLarge.unity` holds a missing script
where `AudioManager` was (GUID `944e3f8cd2ab47d3a41aa6f16f7d3a21`). Remove that
component and add `AudioRoot` to the same GameObject.
`Assets/_Recovery/0.unity` references the same GUID; it is not in the build, so
clean it up or leave it.

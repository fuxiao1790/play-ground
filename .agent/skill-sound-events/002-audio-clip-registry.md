# 002 — Clip id registry on `AudioManager`

## Why

Burst cannot hold an `AudioClip` reference (`Docs/architecture/layer-rules.md`
§*Managed And Unmanaged Data*), so sound events must carry an `int` id resolved
through a managed table.

The table goes on `AudioManager` rather than in a new root type: `AudioManager`
is already the audio root (`Docs/coding-standards.md` §*Root Component Rule*)
and already a static singleton. A separate `CombatAudioRoot` would duplicate
both roles to hold one dictionary.

## Change

Add to `AudioManager`, modelled directly on `CombatVfxRoot.Register`
(`CombatVfxRoot.cs:52-110`):

```csharp
private readonly Dictionary<AudioClip, int> idsByClip = new();
private readonly List<AudioClip> clipsById = new();   // index 0 unused

// Returns 0 for null. Stable across repeated registration of the same clip.
public int Register(AudioClip clip);

// False for id <= 0 or out of range.
public bool TryGetClip(int clipId, out AudioClip clip);
```

Conventions to match exactly:

- **Id `0` means "none".** Same as `CombatVfxRoot.Register` returning `0` for a
  null asset, same as `RuntimeSkillDefinition.RenderId` ("0 means 'no render'").
  Producers guard with `if (clipId <= 0) return;` as `VfxEmit.cs:15` does.
- **Dedupe by clip reference.** `Register` returns the existing id when the clip
  is already known. This is what keeps ids stable across
  `SkillDriver.CompileAndRegister`, which re-runs on every loadout edit
  (`SkillDriver.cs:191`) — exactly why `CombatVfxRoot` keeps `idsByAsset`.
- Ids are dense and start at `1`.

`Clear()` must **not** drop registrations — it stops voices, and clip ids are
authored identity, not playback state. Registration lifetime is the
`AudioManager` instance.

## Acceptance Criteria

- `Register(null)` returns `0` without allocating an entry.
- `Register(clip)` twice returns the same id.
- `TryGetClip` round-trips every registered id; returns `false` for `0`,
  negatives, and out-of-range ids.
- `Clear()` leaves registrations intact.
- No change to existing pool, culling, or counter behavior.

## Tests

EditMode, no scene needed — the registry is a pure data structure. Add
`Assets/Tests/EditMode/AudioClipRegistryEditModeTests.cs` covering the five
criteria above. This is the first audio test in the project.

Note `AudioManager` is a `MonoBehaviour`; instantiate it in the test with
`new GameObject().AddComponent<AudioManager>()` and destroy it in teardown, or
extract the registry into a plain nested class if that proves cleaner. Prefer
testing through the public `AudioManager` API rather than adding a test-only
accessor (`coding-standards.md` §*Test Hooks*).

## Dependencies

[001](./001-move-audiomanager-to-sim.md).

## Scope

Small — roughly 30 lines plus tests.

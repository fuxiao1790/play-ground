# 003 — Skill cast sound: authoring → clip id → enqueue

The first and, for now, only producer.

## Why

Skill cast is the sound the player most needs to hear and the one this work was
scoped to. It is also **not** low-count: `MobRoot.cs:505` calls
`skillDriver.Tick(true, ...)`, and `SkillDriver.Tick` fires every ready slot every
frame while `fireHeld` (`SkillDriver.cs:100-129`), so the rate is
`mobCount × slots / recovery`.

## Change

### Authoring — mirror the VFX chain exactly

Sound is authored **on the basic prefab**, beside the VFX asset it accompanies,
and travels the same five-stage path VFX already travels. Do **not** put clips on
the `Skill` ScriptableObject; nothing else presentation-shaped lives there.

The existing chain, for reference — this is the shape being copied:

| Stage | VFX | File |
|---|---|---|
| 1. Prefab holds the asset | `[SerializeField] VisualEffectAsset spawnEffect` | `BasicAoePrefab.cs:20` |
| 2. Definition passes it through | `SpawnEffect => prefab != null ? prefab.SpawnEffect : null` | `SkillDefinition.cs:99` |
| 3. Base declares it abstract | `public abstract VisualEffectAsset SpawnEffect { get; }` | `SkillDefinition.cs:79` |
| 4. Compiler copies to runtime | `SpawnEffect = a.SpawnEffect` | `SkillSetCompiler.cs:362` |
| 5. Driver resolves to an int id | `vfxRoot.Register(definition.SpawnEffect, …)` → `AoeVfxIds` | `SkillDriver.cs:1250` |

#### 1. Prefab fields

Add to each prefab class, beside its existing `spawnEffect`:

```csharp
[SerializeField] private AudioClip spawnSound;
[SerializeField, Min(0f)] private float spawnSoundRadius;   // 0 = AudioRoot default
public AudioClip SpawnSound => spawnSound;
public float SpawnSoundRadius => spawnSoundRadius;
```

- `BasicAoePrefab` (`Assets/Scripts/Skills/Validator/BasicAoePrefab.cs`)
- `LingeringAoePrefab` (same folder)
- `TargetedPrefab` (same folder)
- `BasicAttackPrefab` (`Assets/Scripts/System/Authoring/BasicAttackPrefab.cs`)

**Note the asymmetry:** `BasicAttackPrefab` authors no VFX today — projectiles
have no `spawnEffect` and there is no `RegisterProjectileVfx`. It gets the sound
slot anyway, because a projectile skill must be audible when cast. It is also in
`PlayGround.Sim` rather than GameLogic; `AudioClip` is `UnityEngine`, so the
field is legal there, and it needs no reference to `AudioRoot`.

**Only `spawnSound` is added.** The VFX slot set is Spawn / Hit / Expire / Pulse
/ Arming, and the pairing is deliberately left incomplete: a `hitSound` field
with no producer is an authored clip that silently never plays — worse in the
inspector than an absent field. Each further slot lands beside its VFX sibling
when its producer does, which the chain below makes a one-line change per stage.

#### 2–3. Definition pass-through

Add `SpawnSound` / `SpawnSoundRadius` beside the existing `SpawnEffect`
pass-throughs, `abstract` on `AoeDefinitionBase` (`SkillDefinition.cs:79-88`) and
concrete on `AoeDefinition` (`:99`), `LingeringAoeDefinition` (`:123`), the
targeted definition, and the projectile definition.

#### 4. Compiler copy

In `SkillSetCompiler`, beside `SpawnEffect = a.SpawnEffect` (`:362`), copy
`SpawnSound` and `SpawnSoundRadius` into the runtime definition.

#### 5. Runtime ids

Add to `RuntimeSkillDefinition`
(`Assets/Scripts/Skills/Runtime/RuntimeSkillDefinition.cs`), beside `RenderId`
(`:10`) and following its convention — on the **base**, like `RenderId`, because
all four skill kinds carry sound, unlike `VfxIds` which sits on the AOE and
targeted subtypes only:

```csharp
// Sound identities from the AudioRoot clip id space; 0 means "no sound".
public SkillSoundIds SoundIds { get; set; }

// Spatial cull radius for the spawn sound; 0 means "use the AudioRoot default".
public float SpawnSoundRadius { get; set; }
```

`SkillSoundIds` is defined in [001](./001-audio-root.md) and mirrors `AoeVfxIds`
(`AoeVfxEcsComponents.cs:19-26`). It holds only `SpawnId` today and is a struct
rather than a bare `int` precisely so the remaining slots cost one field each.

### Resolution

Add `RegisterSounds()` to `SkillDriver`, called from `CompileAndRegister`
(`SkillDriver.cs:196`) alongside `RegisterProjectileTypes` / `RegisterAoeTypes` /
`RegisterTargetedTypes` / `RegisterSpawnTemplates`, and modelled directly on
`RegisterAoeVfx` (`SkillDriver.cs:1240-1260`):

```csharp
def.SoundIds = new SkillSoundIds
{
    SpawnId = audioRoot != null ? audioRoot.Register(def.SpawnSound) : 0
};
```

Registration must happen here, not at cast time.
`Docs/architecture/layer-rules.md` §*Runtime, Authoring, And Configuration*
requires runtime data to be copied from authoring data before simulation, and
`coding-standards.md` §*Update Timing* forbids hiding setup inside repeated
runtime calls. `Register` dedupes ([001](./001-audio-root.md)), so re-running on
every loadout edit is free.

Only root slots need an id resolved for this task — the emitter is the cast site,
not nested spawn children. Do not walk the `Register*Recursive` trees yet;
children become audible when an ECS producer emits at their spawn, which is the
deferred lane in [index.md](./index.md) Decision 3.

### Emission

In `SkillDriver.Tick`, inside the existing `SpawnMarker` block
(`SkillDriver.cs:112-125`), next to `SkillSpawnTranslator.Spawn`:

```csharp
RuntimeSkillDefinition def = compiledSlots[i];
if (def.SoundIds.SpawnId > 0 && audioRoot != null)
{
    audioRoot.Enqueue(new SoundEvent
    {
        ClipId = def.SoundIds.SpawnId,
        Position = new float2(transform.position.x, transform.position.y),
        Velocity = default,           // see below
        AudibleRadius = def.SpawnSoundRadius,
        Category = SoundCategory.Cast,
        Priority = castPriority
    });
}
```

Guard with `> 0` exactly as `VfxEmit.cs:15` guards `vfxId`.

`AudibleRadius` is `0` for any prefab authored before the field existed, which
means "use `defaultAudibleRadius`" ([002](./002-drain-and-selection.md)) — so no
asset migration is needed.

`Category` is `Cast` even though the authored field is `spawnSound`. The field
name follows the prefab's VFX naming, which describes *what the prefab does*; the
category describes *what the listener is hearing*, and from the player's side the
root cast is a cast. When an ECS producer later emits the same `SpawnId` for
interval and on-hit children, those carry `SoundCategory.Spawn`.

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

- A prefab with no `spawnSound` produces `SoundIds.SpawnId == 0` and enqueues
  nothing.
- A skill whose definition has **no prefab assigned at all** resolves to `0` and
  does not throw — the pass-throughs already null-guard (`SkillDefinition.cs:99`),
  and sound must match that tolerance.
- Clip ids survive a loadout edit — `CompileAndRegister` re-runs and the same
  clip yields the same id.
- All four prefab types can author a spawn sound, including `BasicAttackPrefab`,
  which has no VFX sibling.
- `PlayGround.Sim.asmdef` is still unchanged after adding the `BasicAttackPrefab`
  field.
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

Add `Assets/Tests/EditMode/SkillCastSoundEditModeTests.cs` covering the authoring
chain end to end: a prefab with a `spawnSound` compiles to
`SoundIds.SpawnId > 0`, the id is stable across a recompile, `spawnSoundRadius`
reaches `RuntimeSkillDefinition.SpawnSoundRadius`, and a definition with a null
prefab resolves to `0` without throwing.

## Dependencies

[002](./002-drain-and-selection.md).

## Scope

Medium — two fields on each of four prefab classes, matching pass-throughs on the
definitions, two compiler copies, one `RegisterSounds` pass, one emission block,
plus the `OnEnable` wiring. Wide but shallow: every stage is a copy of the line
above it in the VFX chain.

## Editor Work (User)

Assign the `AudioRoot` reference on each `SkillDriver` (player and mob prefabs).

Assign `spawnSound` on the **prefab** for each skill you want audible —
`BasicAoePrefab`, `LingeringAoePrefab`, `TargetedPrefab`, or
`BasicAttackPrefab` — beside the `spawnEffect` VFX slot you already fill in.
Not on the `Skill` asset. Set `spawnSoundRadius` only where the default reach is
wrong; `0` means "use the project default". Prefabs with no clip stay silent by
design, so nothing breaks if you only assign a few.

Make sure an `AudioRoot` exists in the scene under test with its `listenerObject`
assigned.

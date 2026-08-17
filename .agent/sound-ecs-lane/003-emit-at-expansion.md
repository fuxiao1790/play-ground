# 003 — Emit at expansion and arming; remove the cast-site enqueue

The task that makes every spawn audible.

## Change

### Emit sites

Every site that already emits spawn VFX gains the sound call beside it. The model
is `AoeSpawnExpansionSystem.cs:100-110`:

```csharp
// While arming, only the telegraph plays here; the spawn
// burst is deferred to arm completion in CombatArmingSystem.
int vfxId = spawned.ArmSeconds > 0f
    ? spawned.VfxIds.ArmingId
    : spawned.VfxIds.SpawnId;
VfxTimingData timing = AoeSpawnApplyUtility.VfxTimingFor(spawned);
VfxEmit.Enqueue(vfxId, pos, spawned.AreaSize, timing, circularVfxPending, timedCircularVfxPending);
```

beside which:

```csharp
SoundEmit.Enqueue(
    spawned.SoundIds.SpawnId,
    pos,
    spawned.SpawnSoundRadius,
    SoundCategory.Spawn,
    spawned.Faction,
    soundsPending);
```

| System | Note |
|---|---|
| `AoeSpawnExpansionSystem.cs:104` | the model above |
| `ProjectileSpawnExpansionSystem` | projectiles emit no VFX — this is their first presentation emit |
| `TargetedSpawnExpansionSystem` | |
| `CombatArmingSystem.cs:108` | the deferred spawn burst after arming completes |

**Mirror the arming branch.** `AoeSpawnExpansionSystem` deliberately plays only
the telegraph when `ArmSeconds > 0f` and defers the burst to
`CombatArmingSystem`. Sound must follow the same branch or an armed AOE sounds
twice, or sounds at placement when the design says it sounds at arm. There is no
arming sound slot — [002](./002-ids-to-spawn-commands.md) carries only
`SpawnId` — so an arming AOE stays silent until arm completion, where
`CombatArmingSystem` emits `SpawnId`.

### Lane wiring

Each emitting system takes `SoundEventSingleton.Events.AsParallelWriter()` as a
job field and combines its handle into `ProducerHandle` on the main thread, the
same way the VFX queues are already threaded through these systems. Never reach
into `SoundEventDispatchSystem` (`coding-standards.md` §*System Encapsulation*).

### Remove the cast-site enqueue

Delete `SkillDriver.cs:134-148` — the `RuntimeSkillDefinition definition = …`
block through the closing brace of the `audioRoot.Enqueue(...)` call.

**This is not optional cleanup.** Root casts reach expansion through
`ExternalSpawnGateSystem` like every other spawn, so leaving it in sounds every
root cast twice.

`SkillDriver` keeps its `audioRoot` field and its `OnEnable` resolution —
`RegisterSounds` still needs them. Only the `Tick` emission goes.

## What this fixes for free

- **Mana-rejected casts become silent.** `ExternalSpawnGateSystem` rejects on
  `SpawnRejectionReason.InsufficientMana` before expansion, so no event is
  produced. The shipped version emits at the cast site and sounds a cast that
  `ReceiveSpawnRejected` (`SkillDriver.cs:158`) later refunds.
- **Priority covers nested spawns.** `SoundEmit` derives it from
  `CombatFaction` on the spawn command (`AoeSpawnPipeline.cs:59`) rather than
  from `SkillDriver`'s serialized `faction` (`SkillDriver.cs:146`), which nested
  spawns never see.
- **Volleys and echoes stay one cast, many spawns.** A `Count`-5 volley or an
  `EchoCount`-3 AOE now emits per spawned entity. That is correct: the per-clip
  frame cap and repeat falloff already shipped in `AudioRoot` are exactly the
  mechanism for collapsing them into one thick sound rather than five stacked
  copies.

## Watch the counters after this lands

Spawn count is far above cast count, so this task is the first real load on the
shipped selection policy. `culledRedundant` should rise sharply — that is working
as designed. **`culledNovel` rising is the signal that matters**; it means a
whole kind went unheard, and the fix is raising `maxActiveSources`, not the
per-clip cap.

## Acceptance Criteria

- A root cast, an interval child, an on-hit spawn, and a stacking detonation all
  sound, given a prefab with a `spawnSound`.
- A root cast sounds **once**, not twice — the cast-site enqueue is gone.
- A spawn whose definition has `SpawnId == 0` emits nothing and costs one branch.
- An armed AOE sounds once, at arm completion, not at placement.
- A cast rejected for insufficient mana produces no sound.
- Player-faction spawns outrank mob-faction spawns, including nested spawns.
- No `AudioClip` or managed reference enters any job.
- Emitting systems combine into `ProducerHandle` on the main thread; none reads
  another system's fields.
- A missing `AudioRoot` yields silence and one setup error, not an exception per
  spawn.
- Holding fire with an interval spawner produces thinned but continuous sound,
  not silence and not a machine-gun.

## Tests

**`SkillCastSoundEditModeTests` (shipped) changes subject.** It asserts the
cast-site enqueue this task deletes. Rewrite its emission assertions: the
authoring and registration cases still hold and must keep passing, but "casting
enqueues a `SoundCategory.Cast` event on `AudioRoot`" is no longer true —
emission moved to ECS and is not observable in EditMode. Do not delete the class;
narrow it to the chain it still covers, and let
[002](./002-ids-to-spawn-commands.md)'s test cover the recursive half.

Emit coverage itself needs a running world and a real pool, so it is verified by
playing with the debug counters visible (`AudioRoot.StatsText`). `accepted`
rising on interval spawns with no cast input is the observable signal.

## Dependencies

[002](./002-ids-to-spawn-commands.md).

## Scope

Medium — four emit sites, lane wiring through each emitting system, one deletion
in `SkillDriver`, and one shipped test narrowed.

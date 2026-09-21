# Presentation And Feedback

## Purpose

Own presentation-time bridge work after simulation data is finalized: managed
target callback resolution, actor feedback, VFX dispatch, batched sprite
submission, audio playback, debug text, and presentation budgets.

## Owns

- `CombatApplyBridge`/presentation bridge behavior that resolves
  `TargetCompanion`.
- `ICombatTarget.ReceiveCombatTick` calls from compact combat results.
- `CombatVfxRoot`, `CombatAoeVfxDispatcher`, VFX Graph buffers, and VFX dispatch
  caps.
- `AudioRoot`, its clip registry, pending sound batch, pooled voices, listener
  binding, and audio budget/selection policy.
- `CombatBatchedRenderSystem` submission through render resources owned by
  combat roots.
- Actor animation/hurt/death feedback after combat result sync.
- Debug overlay display.

## Does Not Own

- Collision hit qualification.
- Health/status aggregation.
- Gameplay meaning of skills, supports, or mob behavior.
- Spawn expansion/apply or ECS entity lifetime.

## Inputs

- `CombatTickResult` presentation data (includes `TickDeltaSeconds` for actors
  that need tick duration).
- Basic and Timed native VFX request queues on the VFX dispatch singleton.
- Clip-bucketed native sound events on `SoundEventSingleton` plus the managed
  per-clip pending buckets.
- Prepared render matrices and per-entity render batch ids.
- Actor target companion references during presentation only.

## Outputs

- Managed target feedback callbacks.
- VFX Graph events and GPU buffer uploads.
- Pooled `AudioSource` voice playback.
- Batched sprite draw submissions.
- Debug/profiling display values.

## Allowed Dependencies

- May read [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md).
- May read [VFX Requests](../contracts/vfx-requests.md).
- May read [Sound Events](../contracts/sound-events.md).
- May read [Render Batch Data](../contracts/render-batch-data.md).
- May resolve managed target companions after simulation finalize.

## Forbidden Dependencies

- Must not make gameplay damage/status decisions in VFX graphs or render code.
- Must not mutate ECS simulation state after finalized results except clearing
  presentation buffers owned by this phase.
- Must not reintroduce per-hit managed damage callbacks for plain aggregate
  damage.

## Main Systems / Modules

- `Assets/Scripts/System/Presentation/CombatApplyBridge.cs`
- `Assets/Scripts/System/Audio/AudioRoot.cs`
- `Assets/Scripts/System/Audio/SoundEventDispatchSystem.cs`
- `Assets/Scripts/System/Rendering/CombatBatchedRenderSystem.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatchSystem.cs`
- `Assets/Scripts/System/Vfx/CombatVfxRoot.cs`
- `Assets/Scripts/System/Vfx/CombatAoeVfxDispatcher.cs`
- `Assets/Scripts/Debugging/DebugOverlay.cs`

## Related Contracts

- [Combat Hit And Tick Results](../contracts/combat-hit-and-tick-results.md)
- [VFX Requests](../contracts/vfx-requests.md)
- [Sound Events](../contracts/sound-events.md)
- [Render Batch Data](../contracts/render-batch-data.md)
- [Target Proxy](../contracts/target-proxy.md)

## Related Flows

- [Runtime Frame](../flows/runtime-frame.md)
- [Collision To Combat Result](../flows/collision-to-combat-result.md)
- [VFX Dispatch](../flows/vfx-dispatch.md)

## Notes / TODOs

- Detailed VFX reference:
  [vfx-system.md](../reference/simulation/vfx-system.md).
- Recurring shared-graph area-size corruption:
  [vfx-shared-graph-area-size-corruption.md](../reference/simulation/vfx-shared-graph-area-size-corruption.md).

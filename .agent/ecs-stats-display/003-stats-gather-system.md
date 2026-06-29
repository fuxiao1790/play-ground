# 003 — Stats gather system

## Goal
Create `CombatStatsGatherSystem : SystemBase` in `PresentationSystemGroup`. It
owns the singleton entity, exposes `Bind`/`Unbind` for the display MonoBehaviour,
and every frame aggregates producer counts, writes `CombatStatsSingleton`, and
pushes the snapshot to the bound display.

## File
- New `Assets/Scripts/System/Stats/CombatStatsGatherSystem.cs`.

## Shape

```csharp
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using PlayGround.System.Vfx;
using Unity.Entities;

namespace PlayGround.System.Stats
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(CombatVfxDispatchSystem))] // keep VFX count same-frame
    public partial class CombatStatsGatherSystem : SystemBase
    {
        private Entity _statsEntity;

        protected override void OnCreate()
        {
            _statsEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(_statsEntity, new CombatStatsSingleton());
            EntityManager.AddComponentObject(_statsEntity, new CombatStatsBinding());
        }

        internal void Bind(CombatStatsDisplay display)
        {
            if (_statsEntity != Entity.Null && EntityManager.Exists(_statsEntity))
                EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity).Display = display;
        }

        internal void Unbind(CombatStatsDisplay display)
        {
            if (_statsEntity != Entity.Null && EntityManager.Exists(_statsEntity))
            {
                var binding = EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity);
                if (binding.Display == display) binding.Display = null;
            }
        }

        protected override void OnUpdate()
        {
            var basicProj = World.GetExistingSystemManaged<BasicProjectileSpawnApplySystem>();
            var childProj = World.GetExistingSystemManaged<ChildSpawnerProjectileSpawnApplySystem>();
            var aoe       = World.GetExistingSystemManaged<AoeSpawnApplySystem>();
            var finalize  = World.GetExistingSystemManaged<CombatApplyFinalizeSystem>();
            var vfx       = World.GetExistingSystemManaged<CombatVfxDispatchSystem>();

            int ecb =
                (basicProj?.LastColdCreateCount ?? 0) +
                (childProj?.LastColdCreateCount ?? 0) +
                (aoe?.LastColdCreateCount ?? 0);
            int reuse =
                (basicProj?.LastReuseCount ?? 0) +
                (childProj?.LastReuseCount ?? 0) +
                (aoe?.LastReuseCount ?? 0);

            var snapshot = new CombatStatsSingleton
            {
                EntitiesSpawnedViaEcb = ecb,
                EntitiesSpawnedViaReuse = reuse,
                HitEventsCreated = finalize?.LastHitEventCount ?? 0,
                VfxEventsCreated = vfx?.LastVfxEventCount ?? 0,
            };

            EntityManager.SetComponentData(_statsEntity, snapshot);

            CombatStatsBinding binding =
                EntityManager.GetComponentObject<CombatStatsBinding>(_statsEntity);
            binding.Display?.Apply(in snapshot);
        }
    }
}
```

## Constraints / rationale
- **Single writer**: this system is the only writer of `CombatStatsSingleton`.
- **No allocation**: reads ints, sets one component, one managed push — conforms
  to the Allocation Rule.
- **Structural change only in `OnCreate`** (mirrors
  `CombatBatchedRenderSystem.OnCreate`); steady state is `SetComponentData`.
- **Null-guarded** `GetExistingSystemManaged` so a world missing any producer (or
  during teardown) does not throw.
- `[UpdateAfter(CombatVfxDispatchSystem)]` makes the VFX count same-frame;
  spawn/hit are already same-frame from `SimulationSystemGroup`. The requirement's
  1-tick allowance is the safety margin, not a dependency.
- Reads the producers' `internal int` fields on the main thread after they ran;
  no job interaction.

## Acceptance criteria
- Singleton entity exists after `OnCreate`; `SystemAPI`/`GetSingleton` for
  `CombatStatsSingleton` resolves to exactly one entity.
- With combat active, the four fields update each frame; spawn fields are
  non-negative and `ecb + reuse == total spawns that frame`.
- `Bind`/`Unbind` set and clear `CombatStatsBinding.Display`.
- No exceptions when no display is bound (`binding.Display?.Apply` no-ops).

## Dependencies
001 (components), 002 (producer fields), 004 (`CombatStatsDisplay.Apply`).

## Scope
Small–medium.

using PlayGround.System.Combat.Application;
using PlayGround.System.Combat.Aoes;
using PlayGround.System.Combat.Collision;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Lifetime;
using PlayGround.System.Combat.Platform;
using PlayGround.System.Combat.Projectiles;
using PlayGround.System.Combat.Rendering;
using PlayGround.System.Combat.Spawning;
using PlayGround.System.Combat.Stats;
using PlayGround.System.Combat.Status;
using PlayGround.System.Combat.Targets;
using PlayGround.System.Combat.Vfx;
using Unity.Collections;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        private CombatAoeVfxDispatcher dispatcher;

        private void Awake()
        {
            dispatcher = new CombatAoeVfxDispatcher(transform);
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            dispatcher?.Dispose();
            dispatcher = null;
        }

        public void Register(int typeId, AoeVfxTrigger trigger, VisualEffectAsset asset, int maxPerFrame = 2048, bool requireAreaSizeContract = false)
        {
            dispatcher?.Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract);
        }

        public void ResetDispatcher()
        {
            dispatcher?.Dispose();
            dispatcher = new CombatAoeVfxDispatcher(transform);
        }

        internal int DrainAndDispatch(ref NativeQueue<AoeVfxSpawnRequest> queue)
        {
            if (dispatcher == null)
            {
                return 0;
            }

            int acceptedSpawnCount = 0;
            while (queue.TryDequeue(out AoeVfxSpawnRequest p))
            {
                if (dispatcher.StageAoeSpawn(p.TypeId, p.Trigger, p.Position, p.AreaSize))
                {
                    acceptedSpawnCount++;
                }
            }

            dispatcher.Dispatch();
            return acceptedSpawnCount;
        }
    }
}

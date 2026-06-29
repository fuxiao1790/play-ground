using Unity.Collections;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        public static CombatVfxRoot Instance { get; private set; }

        private CombatVfxDispatcher dispatcher;

        private void Awake()
        {
            dispatcher = new CombatVfxDispatcher(transform);
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

        public void Register(int typeId, byte trigger, VisualEffectAsset asset, int maxPerFrame = 2048, bool requireAreaSizeContract = false)
        {
            dispatcher?.Register(typeId, trigger, asset, maxPerFrame, requireAreaSizeContract);
        }

        public void ResetDispatcher()
        {
            dispatcher?.Dispose();
            dispatcher = new CombatVfxDispatcher(transform);
        }

        internal void DrainAndDispatch(ref NativeQueue<VfxPendingSpawn> queue)
        {
            if (dispatcher == null)
            {
                return;
            }

            while (queue.TryDequeue(out VfxPendingSpawn p))
            {
                dispatcher.StageSpawn(p.TypeId, p.Trigger, p.Position, p.AreaSize);
            }

            dispatcher.Dispatch();
        }
    }
}

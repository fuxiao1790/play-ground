using PlayGround.System.Common;
using Unity.Collections;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class CombatVfxRoot : MonoBehaviour
    {
        private static readonly CombatVfxRoot[] ByFaction = new CombatVfxRoot[3];

        private CombatVfxDispatcher dispatcher;
        private CombatFaction faction;

        private void Awake()
        {
            dispatcher = new CombatVfxDispatcher(transform);
        }

        private void OnDestroy()
        {
            if (ByFaction[(int)faction] == this)
            {
                ByFaction[(int)faction] = null;
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

        public void BindFaction(CombatFaction boundFaction)
        {
            faction = boundFaction;
            ByFaction[(int)faction] = this;
        }

        internal static bool TryGetByFaction(CombatFaction faction, out CombatVfxRoot root)
        {
            root = faction != CombatFaction.None ? ByFaction[(int)faction] : null;
            return root != null;
        }

        internal void DrainAndDispatch(NativeArray<VfxSpawnRequestElement> requests)
        {
            if (dispatcher == null)
            {
                return;
            }

            for (int i = 0; i < requests.Length; i++)
            {
                VfxSpawnRequestElement e = requests[i];
                dispatcher.StageSpawn(e.TypeId, e.Trigger, e.Position, e.AreaSize);
            }
            dispatcher.Dispatch();
        }
    }
}

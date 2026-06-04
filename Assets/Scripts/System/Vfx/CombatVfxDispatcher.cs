using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    public sealed class VfxTypeResources : global::System.IDisposable
    {
        public VisualEffect Instance;
        public GraphicsBuffer PositionBuffer;
        public NativeList<float2> Staging;
        public int MaxPerFrame;

        public void Dispose()
        {
            // Dispose native memory first so it's freed even if GPU/scene teardown throws.
            if (Staging.IsCreated)
            {
                Staging.Dispose();
            }
            PositionBuffer?.Release();
            PositionBuffer = null;
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }
    }

    public sealed class CombatVfxDispatcher : global::System.IDisposable
    {
        // key = typeId * 256 + trigger
        private readonly Dictionary<int, VfxTypeResources> resources = new();
        private readonly Transform parent;

        public CombatVfxDispatcher(Transform parent)
        {
            this.parent = parent;
        }

        // asset == null means no visual for this trigger; nothing is registered and no memory is allocated.
        public void Register(int typeId, byte trigger, VisualEffectAsset asset, int maxPerFrame)
        {
            if (asset == null)
            {
                return;
            }

            int key = typeId * 256 + trigger;
            if (resources.ContainsKey(key))
            {
                return;
            }

            var go = new GameObject($"Vfx_{typeId}_{trigger}");
            go.transform.SetParent(parent, false);
            VisualEffect vfx = go.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = asset;

            var res = new VfxTypeResources
            {
                MaxPerFrame = maxPerFrame,
                Instance = vfx,
                PositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    maxPerFrame,
                    sizeof(float) * 2),
                Staging = new NativeList<float2>(maxPerFrame, Allocator.Persistent)
            };

            resources[key] = res;
        }

        public void StageSpawn(int typeId, int trigger, float2 position)
        {
            int key = typeId * 256 + trigger;
            if (!resources.TryGetValue(key, out VfxTypeResources res))
            {
                return;
            }

            if (res.Staging.Length < res.MaxPerFrame)
            {
                res.Staging.Add(position);
            }
        }

        public void Dispatch()
        {
            foreach (KeyValuePair<int, VfxTypeResources> pair in resources)
            {
                VfxTypeResources res = pair.Value;
                if (res.Staging.Length == 0)
                {
                    continue;
                }

                res.PositionBuffer.SetData(res.Staging.AsArray(), 0, 0, res.Staging.Length);
                res.Instance.SetGraphicsBuffer("Positions", res.PositionBuffer);
                res.Instance.SetInt("SpawnCount", res.Staging.Length);
                res.Instance.SendEvent("OnSpawn");

                res.Staging.Clear();
            }
        }

        public void Dispose()
        {
            foreach (VfxTypeResources res in resources.Values)
            {
                res.Dispose();
            }
            resources.Clear();
        }
    }
}

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
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    // One VFX instance and everything it owns. Created, staged into, and disposed only by
    // CombatVfxRoot; CombatAoeVfxDispatcher only reads one to push it to the GPU.
    public sealed class AoeVfxTypeResources : global::System.IDisposable
    {
        public const int InitialBufferCapacity = 2048;

        public VisualEffect Instance;
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer AreaSizeBuffer;
        public NativeList<float2> Staging;
        public NativeList<float> AreaSizeStaging;
        public int BufferCapacity;
        public bool RequireAreaSizeContract;

        // Adds paired position/area-size entries in one step so the staging lists stay aligned.
        public bool TryStage(float2 position, float areaSize)
        {
            Staging.Add(position);
            AreaSizeStaging.Add(areaSize);
            return true;
        }

        public void EnsureBufferCapacity(int requiredCapacity)
        {
            if (requiredCapacity <= BufferCapacity)
            {
                return;
            }

            int newCapacity = math.max(InitialBufferCapacity, BufferCapacity);
            while (newCapacity < requiredCapacity)
            {
                newCapacity = checked(newCapacity * 2);
            }

            GraphicsBuffer newPositionBuffer = null;
            GraphicsBuffer newAreaSizeBuffer = null;
            try
            {
                newPositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float) * 2);
                newAreaSizeBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float));
            }
            catch
            {
                newPositionBuffer?.Release();
                newAreaSizeBuffer?.Release();
                throw;
            }

            PositionBuffer?.Release();
            AreaSizeBuffer?.Release();
            PositionBuffer = newPositionBuffer;
            AreaSizeBuffer = newAreaSizeBuffer;
            BufferCapacity = newCapacity;
        }

        public void ClearStaging()
        {
            Staging.Clear();
            AreaSizeStaging.Clear();
        }

        public void Dispose()
        {
            // Dispose native memory first so it's freed even if GPU/scene teardown throws.
            if (Staging.IsCreated)
            {
                Staging.Dispose();
            }
            if (AreaSizeStaging.IsCreated)
            {
                AreaSizeStaging.Dispose();
            }
            PositionBuffer?.Release();
            PositionBuffer = null;
            AreaSizeBuffer?.Release();
            AreaSizeBuffer = null;
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }
    }

    // Owns the VFX graph protocol and nothing else: the exposed-property names, whether a given
    // graph speaks them, and how one staged batch reaches the GPU. Holds no state and no
    // resources — CombatVfxRoot creates, stages, and disposes every AoeVfxTypeResources.
    public sealed class CombatAoeVfxDispatcher
    {
        private const string PositionsPropertyName = "Positions";
        private const string AreaSizePropertyName = "AreaSizes";
        private const string SpawnCountPropertyName = "SpawnCount";
        private const string SpawnEventName = "OnSpawn";

        // Caller is responsible for clearing staging afterwards.
        public void Dispatch(AoeVfxTypeResources res)
        {
            res.EnsureBufferCapacity(res.Staging.Length);
            Vector3 worldPosition = new(0f, 0f, res.Instance.transform.position.z);
            res.Instance.transform.position = worldPosition;
            res.PositionBuffer.SetData(res.Staging.AsArray(), 0, 0, res.Staging.Length);
            res.Instance.SetGraphicsBuffer(PositionsPropertyName, res.PositionBuffer);
            res.AreaSizeBuffer.SetData(res.AreaSizeStaging.AsArray(), 0, 0, res.AreaSizeStaging.Length);
            res.Instance.SetGraphicsBuffer(AreaSizePropertyName, res.AreaSizeBuffer);
            res.Instance.SetInt(SpawnCountPropertyName, res.Staging.Length);
            res.Instance.SendEvent(SpawnEventName);
        }

        public bool ValidateGraphContract(
            VisualEffectAsset asset,
            bool requireAreaSizeContract,
            out string reason)
        {
            var props = new List<VFXExposedProperty>();
            asset.GetExposedProperties(props);

            bool hasPositions = false;
            bool hasSpawnCount = false;
            bool hasAreaSize = false;
            foreach (VFXExposedProperty p in props)
            {
                if (p.name == PositionsPropertyName) hasPositions = p.type == typeof(GraphicsBuffer);
                if (p.name == SpawnCountPropertyName) hasSpawnCount = p.type == typeof(int);
                if (p.name == AreaSizePropertyName) hasAreaSize = p.type == typeof(GraphicsBuffer);
            }

            if (!hasPositions || !hasSpawnCount)
            {
                reason = $"{nameof(CombatAoeVfxDispatcher)} cannot register VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{PositionsPropertyName}', int '{SpawnCountPropertyName}', "
                    + $"and event '{SpawnEventName}'.";
                return false;
            }

            if (requireAreaSizeContract && !hasAreaSize)
            {
                reason = $"{nameof(CombatAoeVfxDispatcher)} cannot register AOE VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{AreaSizePropertyName}'.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}

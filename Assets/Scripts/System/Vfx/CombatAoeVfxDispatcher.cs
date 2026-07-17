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
        public VfxDataShape Shape;
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer AreaSizeBuffer;
        public GraphicsBuffer DurationBuffer;
        public GraphicsBuffer TickIntervalBuffer;
        public int BufferCapacity;

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
            GraphicsBuffer newDurationBuffer = null;
            GraphicsBuffer newTickIntervalBuffer = null;
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

                if (Shape == VfxDataShape.Timed)
                {
                    newDurationBuffer = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        newCapacity,
                        sizeof(float));
                    newTickIntervalBuffer = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        newCapacity,
                        sizeof(float));
                }
            }
            catch
            {
                newPositionBuffer?.Release();
                newAreaSizeBuffer?.Release();
                newDurationBuffer?.Release();
                newTickIntervalBuffer?.Release();
                throw;
            }

            PositionBuffer?.Release();
            AreaSizeBuffer?.Release();
            DurationBuffer?.Release();
            TickIntervalBuffer?.Release();
            PositionBuffer = newPositionBuffer;
            AreaSizeBuffer = newAreaSizeBuffer;
            DurationBuffer = newDurationBuffer;
            TickIntervalBuffer = newTickIntervalBuffer;
            BufferCapacity = newCapacity;
        }

        public void Dispose()
        {
            PositionBuffer?.Release();
            PositionBuffer = null;
            AreaSizeBuffer?.Release();
            AreaSizeBuffer = null;
            DurationBuffer?.Release();
            DurationBuffer = null;
            TickIntervalBuffer?.Release();
            TickIntervalBuffer = null;
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }
    }

    // Owns the VFX graph protocol and nothing else: validation and upload. Holds no state and
    // no resources; CombatVfxRoot creates, stages, and disposes every AoeVfxTypeResources.
    public sealed class CombatAoeVfxDispatcher
    {
        public void Dispatch(AoeVfxTypeResources res, NativeArray<float2> positions, NativeArray<float> areaSizes)
        {
            DispatchBasic(res, positions, areaSizes);
        }

        public void DispatchBasic(
            AoeVfxTypeResources res,
            NativeArray<float2> positions,
            NativeArray<float> areaSizes)
        {
            UploadCommon(res, positions, areaSizes);
            res.Instance.SendEvent(VfxDataShapeTable.SpawnEventName);
        }

        public void DispatchTimed(
            AoeVfxTypeResources res,
            NativeArray<float2> positions,
            NativeArray<float> areaSizes,
            NativeArray<float> durations,
            NativeArray<float> tickIntervals)
        {
            int count = positions.Length;
            UploadCommon(res, positions, areaSizes);
            res.DurationBuffer.SetData(durations, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.DurationsPropertyName, res.DurationBuffer);
            res.TickIntervalBuffer.SetData(tickIntervals, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.TickIntervalsPropertyName, res.TickIntervalBuffer);
            res.Instance.SendEvent(VfxDataShapeTable.SpawnEventName);
        }

        public bool ValidateGraphContract(
            VisualEffectAsset asset,
            VfxDataShape shape,
            out string reason)
        {
            var props = new List<VFXExposedProperty>();
            asset.GetExposedProperties(props);

            IReadOnlyList<VfxDataShapeBuffer> expected = VfxDataShapeTable.BuffersFor(shape);
            var expectedNames = new HashSet<string>();
            for (int i = 0; i < expected.Count; i++)
            {
                expectedNames.Add(expected[i].Name);
                if (!TryFindProperty(props, expected[i].Name, out VFXExposedProperty property))
                {
                    reason = ContractFailure(asset, shape, $"missing GraphicsBuffer '{expected[i].Name}'");
                    return false;
                }

                if (property.type != expected[i].Type)
                {
                    reason = ContractFailure(
                        asset,
                        shape,
                        $"property '{expected[i].Name}' has type '{property.type.Name}', expected '{expected[i].Type.Name}'");
                    return false;
                }
            }

            if (!TryFindProperty(props, VfxDataShapeTable.SpawnCountPropertyName, out VFXExposedProperty spawnCount))
            {
                reason = ContractFailure(asset, shape, $"missing int '{VfxDataShapeTable.SpawnCountPropertyName}'");
                return false;
            }

            if (spawnCount.type != typeof(int))
            {
                reason = ContractFailure(
                    asset,
                    shape,
                    $"property '{VfxDataShapeTable.SpawnCountPropertyName}' has type '{spawnCount.type.Name}', expected 'Int32'");
                return false;
            }

            foreach (VFXExposedProperty p in props)
            {
                if (p.type == typeof(GraphicsBuffer) && !expectedNames.Contains(p.name))
                {
                    reason = ContractFailure(asset, shape, $"unexpected GraphicsBuffer '{p.name}'");
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static void UploadCommon(
            AoeVfxTypeResources res,
            NativeArray<float2> positions,
            NativeArray<float> areaSizes)
        {
            int count = positions.Length;
            res.EnsureBufferCapacity(count);
            Vector3 worldPosition = new(0f, 0f, res.Instance.transform.position.z);
            res.Instance.transform.position = worldPosition;
            res.PositionBuffer.SetData(positions, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.PositionsPropertyName, res.PositionBuffer);
            res.AreaSizeBuffer.SetData(areaSizes, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.AreaSizesPropertyName, res.AreaSizeBuffer);
            res.Instance.SetInt(VfxDataShapeTable.SpawnCountPropertyName, count);
        }

        private static bool TryFindProperty(
            List<VFXExposedProperty> props,
            string propertyName,
            out VFXExposedProperty property)
        {
            for (int i = 0; i < props.Count; i++)
            {
                if (props[i].name == propertyName)
                {
                    property = props[i];
                    return true;
                }
            }

            property = default;
            return false;
        }

        private static string ContractFailure(VisualEffectAsset asset, VfxDataShape shape, string issue) =>
            $"{nameof(CombatAoeVfxDispatcher)} cannot register VFX asset '{asset.name}' as {shape}. "
            + $"Graph contract failure: {issue}. Required event: '{VfxDataShapeTable.SpawnEventName}'.";
    }
}

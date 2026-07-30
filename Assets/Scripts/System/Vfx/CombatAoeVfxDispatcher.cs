using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    // Common resource lifecycle (grow-only GraphicsBuffer capacity, GameObject teardown) shared
    // by every VFX instance. Created, staged into, and disposed only by CombatVfxRoot;
    // CombatAoeVfxDispatcher only reads one to push it to the GPU. Concrete subtypes below own
    // exactly the buffers their VfxDataShape needs - no shape branching at the resource level.
    public abstract class AoeVfxResourcesBase : global::System.IDisposable
    {
        public const int InitialBufferCapacity = 2048;

        public VisualEffect Instance;
        public int BufferCapacity;

        public abstract VfxDataShape Shape { get; }

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

            GrowBuffers(newCapacity);
            BufferCapacity = newCapacity;
        }

        protected abstract void GrowBuffers(int newCapacity);

        public virtual void Dispose()
        {
            if (Instance != null)
            {
                Object.Destroy(Instance.gameObject);
                Instance = null;
            }
        }
    }

    public sealed class CircularVfxResources : AoeVfxResourcesBase
    {
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer AreaSizeBuffer;

        public override VfxDataShape Shape => VfxDataShape.Circular;

        protected override void GrowBuffers(int newCapacity)
        {
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
        }

        public override void Dispose()
        {
            PositionBuffer?.Release();
            PositionBuffer = null;
            AreaSizeBuffer?.Release();
            AreaSizeBuffer = null;
            base.Dispose();
        }
    }

    public sealed class TimedCircularVfxResources : AoeVfxResourcesBase
    {
        public GraphicsBuffer PositionBuffer;
        public GraphicsBuffer AreaSizeBuffer;
        public GraphicsBuffer DurationBuffer;
        public GraphicsBuffer TickIntervalBuffer;

        public override VfxDataShape Shape => VfxDataShape.TimedCircular;

        protected override void GrowBuffers(int newCapacity)
        {
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
                newDurationBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float));
                newTickIntervalBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float));
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
        }

        public override void Dispose()
        {
            PositionBuffer?.Release();
            PositionBuffer = null;
            AreaSizeBuffer?.Release();
            AreaSizeBuffer = null;
            DurationBuffer?.Release();
            DurationBuffer = null;
            TickIntervalBuffer?.Release();
            TickIntervalBuffer = null;
            base.Dispose();
        }
    }

    public sealed class LineSegmentVfxResources : AoeVfxResourcesBase
    {
        public GraphicsBuffer StartPositionBuffer;
        public GraphicsBuffer EndPositionBuffer;
        public GraphicsBuffer WidthBuffer;

        public override VfxDataShape Shape => VfxDataShape.LineSegment;

        protected override void GrowBuffers(int newCapacity)
        {
            GraphicsBuffer newStartPositionBuffer = null;
            GraphicsBuffer newEndPositionBuffer = null;
            GraphicsBuffer newWidthBuffer = null;
            try
            {
                newStartPositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float) * 2);
                newEndPositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float) * 2);
                newWidthBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    newCapacity,
                    sizeof(float));
            }
            catch
            {
                newStartPositionBuffer?.Release();
                newEndPositionBuffer?.Release();
                newWidthBuffer?.Release();
                throw;
            }

            StartPositionBuffer?.Release();
            EndPositionBuffer?.Release();
            WidthBuffer?.Release();
            StartPositionBuffer = newStartPositionBuffer;
            EndPositionBuffer = newEndPositionBuffer;
            WidthBuffer = newWidthBuffer;
        }

        public override void Dispose()
        {
            StartPositionBuffer?.Release();
            StartPositionBuffer = null;
            EndPositionBuffer?.Release();
            EndPositionBuffer = null;
            WidthBuffer?.Release();
            WidthBuffer = null;
            base.Dispose();
        }
    }

    // Owns the VFX graph protocol and nothing else: validation and upload. Holds no state and
    // no resources; CombatVfxRoot creates, stages, and disposes every AoeVfxResourcesBase.
    public sealed class CombatAoeVfxDispatcher
    {
        public void DispatchCircular(
            CircularVfxResources res,
            NativeArray<float2> positions,
            NativeArray<float> areaSizes)
        {
            int count = positions.Length;
            res.EnsureBufferCapacity(count);
            res.Instance.transform.position = new Vector3(0f, 0f, res.Instance.transform.position.z);
            res.PositionBuffer.SetData(positions, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.PositionsPropertyName, res.PositionBuffer);
            res.AreaSizeBuffer.SetData(areaSizes, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.AreaSizesPropertyName, res.AreaSizeBuffer);
            res.Instance.SetInt(VfxDataShapeTable.SpawnCountPropertyName, count);
            res.Instance.SendEvent(VfxDataShapeTable.SpawnEventName);
        }

        public void DispatchTimedCircular(
            TimedCircularVfxResources res,
            NativeArray<float2> positions,
            NativeArray<float> areaSizes,
            NativeArray<float> durations,
            NativeArray<float> tickIntervals)
        {
            int count = positions.Length;
            res.EnsureBufferCapacity(count);
            res.Instance.transform.position = new Vector3(0f, 0f, res.Instance.transform.position.z);
            res.PositionBuffer.SetData(positions, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.PositionsPropertyName, res.PositionBuffer);
            res.AreaSizeBuffer.SetData(areaSizes, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.AreaSizesPropertyName, res.AreaSizeBuffer);
            res.DurationBuffer.SetData(durations, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.DurationsPropertyName, res.DurationBuffer);
            res.TickIntervalBuffer.SetData(tickIntervals, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.TickIntervalsPropertyName, res.TickIntervalBuffer);
            res.Instance.SetInt(VfxDataShapeTable.SpawnCountPropertyName, count);
            res.Instance.SendEvent(VfxDataShapeTable.SpawnEventName);
        }

        public void DispatchLineSegment(
            LineSegmentVfxResources res,
            NativeArray<float2> startPositions,
            NativeArray<float2> endPositions,
            NativeArray<float> widths)
        {
            int count = startPositions.Length;
            res.EnsureBufferCapacity(count);
            res.Instance.transform.position = new Vector3(0f, 0f, res.Instance.transform.position.z);
            res.StartPositionBuffer.SetData(startPositions, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.StartPositionsPropertyName, res.StartPositionBuffer);
            res.EndPositionBuffer.SetData(endPositions, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.EndPositionsPropertyName, res.EndPositionBuffer);
            res.WidthBuffer.SetData(widths, 0, 0, count);
            res.Instance.SetGraphicsBuffer(VfxDataShapeTable.WidthsPropertyName, res.WidthBuffer);
            res.Instance.SetInt(VfxDataShapeTable.SpawnCountPropertyName, count);
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

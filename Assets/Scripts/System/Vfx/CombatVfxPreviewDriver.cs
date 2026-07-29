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
using System.Text;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Combat.Vfx
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualEffect))]
    public sealed class CombatVfxPreviewDriver : MonoBehaviour
    {
        [SerializeField] private VfxDataShape dataShape = VfxDataShape.Circular;
        [SerializeField, Min(1)] private int spawnCount = 32;
        [SerializeField, Min(1)] private int bufferCapacity = 2048;
        [SerializeField, Min(0.02f)] private float intervalSeconds = 0.5f;
        [SerializeField] private bool emitOnEnable = true;
        [SerializeField] private bool emitContinuously = true;
        [SerializeField, Min(0.01f)] private float areaSize = 1f;
        [SerializeField, Min(0.01f)] private float durationSeconds = 1f;
        [SerializeField, Min(0.01f)] private float tickIntervalSeconds = 0.25f;
        [SerializeField] private PreviewPattern pattern = PreviewPattern.Point;
        [SerializeField] private Vector2 centerOffset;
        [SerializeField] private Vector2 endPointOffset = Vector2.right;
        [SerializeField, Min(0.01f)] private float lineWidth = 1f;
        [SerializeField, Min(0f)] private float radius = 1f;
        [SerializeField] private Vector2 rectangleSize = new(2f, 2f);
        [SerializeField] private bool forceEffectWorldOrigin;

        private VisualEffect visualEffect;
        private GraphicsBuffer positionsBuffer;
        private GraphicsBuffer areaSizeBuffer;
        private GraphicsBuffer durationBuffer;
        private GraphicsBuffer tickIntervalBuffer;
        private GraphicsBuffer startPositionBuffer;
        private GraphicsBuffer endPositionBuffer;
        private GraphicsBuffer widthBuffer;
        private Vector2[] positions;
        private float[] areaSizes;
        private float[] durations;
        private float[] tickIntervals;
        private Vector2[] endPositions;
        private float[] widths;
        private double nextEmitTime;
        private bool missingContractLogged;

        private enum PreviewPattern
        {
            Point,
            Circle,
            RandomDisc,
            RandomRectangle,
            Grid
        }

        private void Awake()
        {
            BindVisualEffect();
        }

        private void OnEnable()
        {
            BindVisualEffect();
            EnsureBuffers();
            nextEmitTime = CurrentTime() + intervalSeconds;

            if (emitOnEnable)
            {
                EmitPreview();
            }
        }

        private void Update()
        {
            if (!emitContinuously)
            {
                return;
            }

            double now = CurrentTime();
            if (now < nextEmitTime)
            {
                RequestEditorUpdate();
                return;
            }

            nextEmitTime = now + intervalSeconds;
            EmitPreview();
            RequestEditorUpdate();
        }

        private void OnDisable() => ReleaseBuffers();

        private void OnDestroy() => ReleaseBuffers();

        private void OnValidate()
        {
            spawnCount = Mathf.Max(1, spawnCount);
            bufferCapacity = Mathf.Max(1, bufferCapacity);
            intervalSeconds = Mathf.Max(0.02f, intervalSeconds);
            areaSize = Mathf.Max(0.01f, areaSize);
            durationSeconds = Mathf.Max(0.01f, durationSeconds);
            tickIntervalSeconds = Mathf.Max(0.01f, tickIntervalSeconds);
            lineWidth = Mathf.Max(0.01f, lineWidth);
            radius = Mathf.Max(0f, radius);
            rectangleSize = new Vector2(Mathf.Max(0f, rectangleSize.x), Mathf.Max(0f, rectangleSize.y));
            nextEmitTime = 0d;
            ReleaseBuffers();
            RequestEditorUpdate();
        }

        [ContextMenu("Emit Preview")]
        public void EmitPreview()
        {
            BindVisualEffect();
            if (visualEffect == null || !ValidateGraphContract())
            {
                return;
            }

            int count = Mathf.Clamp(spawnCount, 1, bufferCapacity);
            EnsureBuffers();
            FillPositions(count);

            if (forceEffectWorldOrigin)
            {
                visualEffect.transform.position = new Vector3(0f, 0f, visualEffect.transform.position.z);
            }

            if (dataShape == VfxDataShape.LineSegment)
            {
                FillEndPositions(count);
                startPositionBuffer.SetData(positions, 0, 0, count);
                visualEffect.SetGraphicsBuffer(VfxDataShapeTable.StartPositionsPropertyName, startPositionBuffer);
            endPositionBuffer.SetData(endPositions, 0, 0, count);
            visualEffect.SetGraphicsBuffer(VfxDataShapeTable.EndPositionsPropertyName, endPositionBuffer);
            FillLineWidths(count);
            widthBuffer.SetData(widths, 0, 0, count);
            visualEffect.SetGraphicsBuffer(VfxDataShapeTable.WidthsPropertyName, widthBuffer);
            }
            else
            {
                positionsBuffer.SetData(positions, 0, 0, count);
                visualEffect.SetGraphicsBuffer(VfxDataShapeTable.PositionsPropertyName, positionsBuffer);

                FillAreaSize(count);
                areaSizeBuffer.SetData(areaSizes, 0, 0, count);
                visualEffect.SetGraphicsBuffer(VfxDataShapeTable.AreaSizesPropertyName, areaSizeBuffer);

                if (dataShape == VfxDataShape.TimedCircular)
                {
                    FillTimedCircularData(count);
                    durationBuffer.SetData(durations, 0, 0, count);
                    visualEffect.SetGraphicsBuffer(VfxDataShapeTable.DurationsPropertyName, durationBuffer);
                    tickIntervalBuffer.SetData(tickIntervals, 0, 0, count);
                    visualEffect.SetGraphicsBuffer(VfxDataShapeTable.TickIntervalsPropertyName, tickIntervalBuffer);
                }
            }

            visualEffect.SetInt(VfxDataShapeTable.SpawnCountPropertyName, count);
            visualEffect.SendEvent(VfxDataShapeTable.SpawnEventName);
        }

        private void BindVisualEffect()
        {
            if (visualEffect == null)
            {
                visualEffect = GetComponent<VisualEffect>();
            }
        }

        private void EnsureBuffers()
        {
            int capacity = Mathf.Max(1, bufferCapacity);
            bool positionsReady = positionsBuffer != null && positionsBuffer.count == capacity;
            bool areaSizesReady = areaSizeBuffer != null && areaSizeBuffer.count == capacity;
            bool timedCircularBuffersReady = dataShape == VfxDataShape.TimedCircular
                ? durationBuffer != null
                    && durationBuffer.count == capacity
                    && tickIntervalBuffer != null
                    && tickIntervalBuffer.count == capacity
                : durationBuffer == null && tickIntervalBuffer == null;
            bool lineSegmentBuffersReady = dataShape == VfxDataShape.LineSegment
                ? startPositionBuffer != null
                    && startPositionBuffer.count == capacity
                    && endPositionBuffer != null
                    && endPositionBuffer.count == capacity
                    && widthBuffer != null
                    && widthBuffer.count == capacity
                : startPositionBuffer == null && endPositionBuffer == null && widthBuffer == null;
            if (positionsReady && areaSizesReady && timedCircularBuffersReady && lineSegmentBuffersReady)
            {
                return;
            }

            ReleaseBuffers();
            positions = new Vector2[capacity];
            positionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                sizeof(float) * 2);

            areaSizes = new float[capacity];
            areaSizeBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                sizeof(float));

            if (dataShape == VfxDataShape.TimedCircular)
            {
                durations = new float[capacity];
                tickIntervals = new float[capacity];
                durationBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    capacity,
                    sizeof(float));
                tickIntervalBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    capacity,
                    sizeof(float));
            }
            else if (dataShape == VfxDataShape.LineSegment)
            {
                endPositions = new Vector2[capacity];
                widths = new float[capacity];
                startPositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    capacity,
                    sizeof(float) * 2);
                endPositionBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    capacity,
                    sizeof(float) * 2);
                widthBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    capacity,
                    sizeof(float));
            }
        }

        private void ReleaseBuffers()
        {
            positionsBuffer?.Release();
            positionsBuffer = null;
            areaSizeBuffer?.Release();
            areaSizeBuffer = null;
            durationBuffer?.Release();
            durationBuffer = null;
            tickIntervalBuffer?.Release();
            tickIntervalBuffer = null;
            startPositionBuffer?.Release();
            startPositionBuffer = null;
            endPositionBuffer?.Release();
            endPositionBuffer = null;
            widthBuffer?.Release();
            widthBuffer = null;
            positions = null;
            areaSizes = null;
            durations = null;
            tickIntervals = null;
            endPositions = null;
            widths = null;
        }

        private bool ValidateGraphContract()
        {
            IReadOnlyList<VfxDataShapeBuffer> buffers = VfxDataShapeTable.BuffersFor(dataShape);
            bool[] hasBuffers = new bool[buffers.Count];
            bool hasAllBuffers = true;
            for (int i = 0; i < buffers.Count; i++)
            {
                hasBuffers[i] = visualEffect.HasGraphicsBuffer(buffers[i].Name);
                hasAllBuffers &= hasBuffers[i];
            }

            bool hasSpawnCount = visualEffect.HasInt(VfxDataShapeTable.SpawnCountPropertyName);
            if (hasAllBuffers && hasSpawnCount)
            {
                missingContractLogged = false;
                return true;
            }

            if (!missingContractLogged)
            {
                string missingProperties = MissingPropertiesMessage(buffers, hasBuffers, hasSpawnCount);
                Debug.LogError(
                    $"{nameof(CombatVfxPreviewDriver)} on {name} cannot preview this {dataShape} VFX graph. "
                    + $"Missing exposed properties: {missingProperties}. "
                    + $"Required event: '{VfxDataShapeTable.SpawnEventName}'.",
                    this);
                missingContractLogged = true;
            }

            return false;
        }

        private static string MissingPropertiesMessage(
            IReadOnlyList<VfxDataShapeBuffer> buffers,
            bool[] hasBuffers,
            bool hasSpawnCount)
        {
            StringBuilder builder = new();
            for (int i = 0; i < buffers.Count; i++)
            {
                AppendMissing(builder, hasBuffers[i], $"GraphicsBuffer '{buffers[i].Name}'");
            }

            AppendMissing(builder, hasSpawnCount, $"int '{VfxDataShapeTable.SpawnCountPropertyName}'");
            return builder.ToString();
        }

        private static void AppendMissing(StringBuilder builder, bool hasProperty, string label)
        {
            if (hasProperty)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(label);
        }

        private void FillPositions(int count)
        {
            Vector2 center = (Vector2)transform.position + centerOffset;
            switch (pattern)
            {
                case PreviewPattern.Circle:
                    FillCircle(count, center);
                    break;
                case PreviewPattern.RandomDisc:
                    FillRandomDisc(count, center);
                    break;
                case PreviewPattern.RandomRectangle:
                    FillRandomRectangle(count, center);
                    break;
                case PreviewPattern.Grid:
                    FillGrid(count, center);
                    break;
                default:
                    FillPoint(count, center);
                    break;
            }
        }

        private void FillPoint(int count, Vector2 center)
        {
            for (int i = 0; i < count; i++)
            {
                positions[i] = center;
            }
        }

        private void FillCircle(int count, Vector2 center)
        {
            float safeRadius = Mathf.Max(0f, radius);
            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)count : 0f;
                float angle = t * Mathf.PI * 2f;
                positions[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * safeRadius;
            }
        }

        private void FillRandomDisc(int count, Vector2 center)
        {
            float safeRadius = Mathf.Max(0f, radius);
            for (int i = 0; i < count; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float distance = Mathf.Sqrt(Random.value) * safeRadius;
                positions[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            }
        }

        private void FillRandomRectangle(int count, Vector2 center)
        {
            Vector2 halfSize = new(Mathf.Max(0f, rectangleSize.x) * 0.5f, Mathf.Max(0f, rectangleSize.y) * 0.5f);
            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector2(
                    Random.Range(center.x - halfSize.x, center.x + halfSize.x),
                    Random.Range(center.y - halfSize.y, center.y + halfSize.y));
            }
        }

        private void FillGrid(int count, Vector2 center)
        {
            int columns = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(count)));
            int rows = Mathf.Max(1, Mathf.CeilToInt(count / (float)columns));
            Vector2 size = new(Mathf.Max(0f, rectangleSize.x), Mathf.Max(0f, rectangleSize.y));
            Vector2 origin = center - size * 0.5f;
            for (int i = 0; i < count; i++)
            {
                int x = i % columns;
                int y = i / columns;
                float u = columns > 1 ? x / (float)(columns - 1) : 0.5f;
                float v = rows > 1 ? y / (float)(rows - 1) : 0.5f;
                positions[i] = origin + new Vector2(size.x * u, size.y * v);
            }
        }

        private void FillAreaSize(int count)
        {
            float safeAreaSize = Mathf.Max(0.01f, areaSize);
            for (int i = 0; i < count; i++)
            {
                areaSizes[i] = safeAreaSize;
            }
        }

        private void FillEndPositions(int count)
        {
            for (int i = 0; i < count; i++)
            {
                endPositions[i] = positions[i] + endPointOffset;
            }
        }

        private void FillLineWidths(int count)
        {
            float safeLineWidth = Mathf.Max(0.01f, lineWidth);
            for (int i = 0; i < count; i++)
            {
                widths[i] = safeLineWidth;
            }
        }

        private void FillTimedCircularData(int count)
        {
            float safeDuration = Mathf.Max(0.01f, durationSeconds);
            float safeTickInterval = Mathf.Max(0.01f, tickIntervalSeconds);
            for (int i = 0; i < count; i++)
            {
                durations[i] = safeDuration;
                tickIntervals[i] = safeTickInterval;
            }
        }

        private static double CurrentTime()
        {
// #if UNITY_EDITOR
//             if (!Application.isPlaying)
//             {
//                 return UnityEditor.EditorApplication.timeSinceStartup;
//             }
// #endif
            return Time.timeAsDouble;
        }

        private static void RequestEditorUpdate()
        {
// #if UNITY_EDITOR
//             if (!Application.isPlaying)
//             {
//                 UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
//             }
// #endif
        }
    }
}

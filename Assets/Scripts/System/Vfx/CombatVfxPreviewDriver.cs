using System.Text;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.System.Vfx
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualEffect))]
    public sealed class CombatVfxPreviewDriver : MonoBehaviour
    {
        private const string PositionsPropertyName = "Positions";
        private const string AreaSizePropertyName = "AreaSizes";
        private const string SpawnCountPropertyName = "SpawnCount";
        private const string SpawnEventName = "OnSpawn";

        [SerializeField, Min(1)] private int spawnCount = 32;
        [SerializeField, Min(1)] private int bufferCapacity = 2048;
        [SerializeField, Min(0.02f)] private float intervalSeconds = 0.5f;
        [SerializeField] private bool emitOnEnable = true;
        [SerializeField] private bool emitContinuously = true;
        [SerializeField] private bool provideAreaSize;
        [SerializeField, Min(0.01f)] private float areaSize = 1f;
        [SerializeField] private PreviewPattern pattern = PreviewPattern.Point;
        [SerializeField] private Vector2 centerOffset;
        [SerializeField, Min(0f)] private float radius = 1f;
        [SerializeField] private Vector2 rectangleSize = new(2f, 2f);
        [SerializeField] private bool forceEffectWorldOrigin;

        private VisualEffect visualEffect;
        private GraphicsBuffer positionsBuffer;
        private GraphicsBuffer areaSizeBuffer;
        private Vector2[] positions;
        private float[] areaSizes;
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
            radius = Mathf.Max(0f, radius);
            rectangleSize = new Vector2(Mathf.Max(0f, rectangleSize.x), Mathf.Max(0f, rectangleSize.y));
            nextEmitTime = 0d;
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

            positionsBuffer.SetData(positions, 0, 0, count);
            visualEffect.SetGraphicsBuffer(PositionsPropertyName, positionsBuffer);

            if (provideAreaSize)
            {
                FillAreaSize(count);
                areaSizeBuffer.SetData(areaSizes, 0, 0, count);
                visualEffect.SetGraphicsBuffer(AreaSizePropertyName, areaSizeBuffer);
            }

            visualEffect.SetInt(SpawnCountPropertyName, count);
            visualEffect.SendEvent(SpawnEventName);
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
            bool areaSizesReady = provideAreaSize
                ? areaSizeBuffer != null && areaSizeBuffer.count == capacity
                : areaSizeBuffer == null;
            if (positionsReady && areaSizesReady)
            {
                return;
            }

            ReleaseBuffers();
            positions = new Vector2[capacity];
            positionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                sizeof(float) * 2);

            if (provideAreaSize)
            {
                areaSizes = new float[capacity];
                areaSizeBuffer = new GraphicsBuffer(
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
            positions = null;
            areaSizes = null;
        }

        private bool ValidateGraphContract()
        {
            bool hasPositions = visualEffect.HasGraphicsBuffer(PositionsPropertyName);
            bool hasSpawnCount = visualEffect.HasInt(SpawnCountPropertyName);
            bool hasAreaSize = !provideAreaSize || visualEffect.HasGraphicsBuffer(AreaSizePropertyName);
            if (hasPositions && hasSpawnCount && hasAreaSize)
            {
                missingContractLogged = false;
                return true;
            }

            if (!missingContractLogged)
            {
                string missingProperties = MissingPropertiesMessage(hasPositions, hasSpawnCount, hasAreaSize);
                Debug.LogError(
                    $"{nameof(CombatVfxPreviewDriver)} on {name} cannot preview this VFX graph. "
                    + $"Missing exposed properties: {missingProperties}. "
                    + $"Required event: '{SpawnEventName}'.",
                    this);
                missingContractLogged = true;
            }

            return false;
        }

        private static string MissingPropertiesMessage(bool hasPositions, bool hasSpawnCount, bool hasAreaSize)
        {
            StringBuilder builder = new();
            AppendMissing(builder, hasPositions, $"GraphicsBuffer '{PositionsPropertyName}'");
            AppendMissing(builder, hasSpawnCount, $"int '{SpawnCountPropertyName}'");
            AppendMissing(builder, hasAreaSize, $"GraphicsBuffer '{AreaSizePropertyName}'");
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

        private static double CurrentTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return UnityEditor.EditorApplication.timeSinceStartup;
            }
#endif
            return Time.timeAsDouble;
        }

        private static void RequestEditorUpdate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }
    }
}

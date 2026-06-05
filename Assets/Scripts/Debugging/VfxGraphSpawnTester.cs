using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Debugging
{
    public sealed class VfxGraphSpawnTester : MonoBehaviour
    {
        private const string PositionsPropertyName = "Positions";
        private const string SpawnCountPropertyName = "SpawnCount";
        private const string SpawnEventName = "OnSpawn";

        [SerializeField] private VisualEffectAsset visualEffectAsset;
        [SerializeField, Min(1)] private int spawnCount = 128;
        [SerializeField, Min(1)] private int bufferCapacity = 100000;
        [SerializeField, Min(0.01f)] private float intervalSeconds = 0.5f;
        [SerializeField] private Vector2 randomAreaCenter = Vector2.zero;
        [SerializeField] private Vector2 randomAreaSize = new(20f, 12f);
        [SerializeField] private bool randomizeAroundMainCamera = true;

        private VisualEffect visualEffect;
        private GraphicsBuffer positionsBuffer;
        private Vector2[] positions;
        private float nextEmitTime;

        private void OnEnable()
        {
            EnsureVisualEffect();
            EnsureBuffer();
            nextEmitTime = Time.time + intervalSeconds;
        }

        private void Update()
        {
            if (Time.time < nextEmitTime)
            {
                return;
            }

            nextEmitTime = Time.time + intervalSeconds;
            Emit();
        }

        private void OnDisable() => ReleaseBuffer();
        private void OnDestroy() => ReleaseBuffer();

        private void Emit()
        {
            if (visualEffectAsset == null || visualEffect == null)
            {
                return;
            }

            int count = Mathf.Clamp(spawnCount, 1, bufferCapacity);
            Vector2 center = SpawnCenter();
            Vector2 halfSize = new(
                Mathf.Max(0f, randomAreaSize.x) * 0.5f,
                Mathf.Max(0f, randomAreaSize.y) * 0.5f);

            EnsureBuffer();
            for (int i = 0; i < count; i++)
            {
                positions[i] = RandomPosition(center, halfSize);
            }

            positionsBuffer.SetData(positions, 0, 0, count);
            visualEffect.SetGraphicsBuffer(PositionsPropertyName, positionsBuffer);
            visualEffect.SetInt(SpawnCountPropertyName, count);
            visualEffect.SendEvent(SpawnEventName);
        }

        private static Vector2 RandomPosition(Vector2 center, Vector2 halfSize)
        {
            return new Vector2(
                Random.Range(center.x - halfSize.x, center.x + halfSize.x),
                Random.Range(center.y - halfSize.y, center.y + halfSize.y));
        }

        private Vector2 SpawnCenter()
        {
            if (!randomizeAroundMainCamera)
            {
                return randomAreaCenter;
            }

            Camera camera = Camera.main != null ? Camera.main : FirstActiveCamera();
            if (camera == null)
            {
                return randomAreaCenter;
            }

            return (Vector2)camera.transform.position + randomAreaCenter;
        }

        private void EnsureVisualEffect()
        {
            visualEffect = GetComponent<VisualEffect>();
            if (visualEffect == null)
            {
                visualEffect = gameObject.AddComponent<VisualEffect>();
            }

            visualEffect.visualEffectAsset = visualEffectAsset;
        }

        private void EnsureBuffer()
        {
            int capacity = Mathf.Max(1, bufferCapacity);
            if (positionsBuffer != null && positionsBuffer.count == capacity)
            {
                return;
            }

            ReleaseBuffer();
            positions = new Vector2[capacity];
            positionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                capacity,
                sizeof(float) * 2);
        }

        private void ReleaseBuffer()
        {
            positionsBuffer?.Release();
            positionsBuffer = null;
            positions = null;
        }

        private static Camera FirstActiveCamera()
        {
            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].isActiveAndEnabled)
                {
                    return cameras[i];
                }
            }

            return null;
        }
    }
}

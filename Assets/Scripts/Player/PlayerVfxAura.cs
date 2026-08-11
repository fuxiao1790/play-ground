using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Player
{
    public sealed class PlayerVfxAura : MonoBehaviour
    {
        private const string PositionsPropertyName = "Positions";
        private const string SpawnCountPropertyName = "SpawnCount";
        private const string SpawnEventName = "OnSpawn";

        [SerializeField] private VisualEffectAsset visualEffectAsset;
        [SerializeField, Min(1)] private int spawnCount = 16;
        [SerializeField, Min(0f)] private float minRadius = 0.5f;
        [SerializeField, Min(0f)] private float maxRadius = 3f;
        [SerializeField, Min(0.01f)] private float intervalSeconds = 0.4f;

        private VisualEffect visualEffect;
        private GraphicsBuffer positionsBuffer;
        private NativeList<float2> staging;
        private float nextEmitTime;

        private void Awake()
        {
            if (visualEffectAsset == null)
            {
                throw new MissingReferenceException($"{nameof(PlayerVfxAura)} on {name} needs a VisualEffectAsset.");
            }

            visualEffect = GetComponent<VisualEffect>();
            if (visualEffect == null)
            {
                visualEffect = gameObject.AddComponent<VisualEffect>();
            }

            visualEffect.visualEffectAsset = visualEffectAsset;

            if (!ValidateGraphContract(visualEffect, visualEffectAsset))
            {
                enabled = false;
                return;
            }

            int capacity = Mathf.Max(1, spawnCount);
            positionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, sizeof(float) * 2);
            staging = new NativeList<float2>(capacity, Allocator.Persistent);
            PrepareBatch();
        }

        private void OnEnable()
        {
            nextEmitTime = Time.time + intervalSeconds;
        }

        private void Update()
        {
            if (Time.deltaTime <= 0f)
            {
                return;
            }

            if (Time.time < nextEmitTime)
            {
                return;
            }

            nextEmitTime = Time.time + intervalSeconds;
            Emit();
        }

        private void OnDestroy()
        {
            if (staging.IsCreated)
            {
                staging.Dispose();
            }

            positionsBuffer?.Release();
            positionsBuffer = null;
        }

        private void OnValidate()
        {
            if (visualEffectAsset == null)
            {
                Debug.LogWarning($"{nameof(PlayerVfxAura)} on {name}: VisualEffectAsset is not assigned.", this);
            }

            if (maxRadius <= 0f)
            {
                Debug.LogWarning($"{nameof(PlayerVfxAura)} on {name}: maxRadius is 0 — all spawn points will collapse to the player origin.", this);
            }
        }

        private void Emit()
        {
            positionsBuffer.SetData(staging.AsArray(), 0, 0, staging.Length);
            visualEffect.SetGraphicsBuffer(PositionsPropertyName, positionsBuffer);
            visualEffect.SetInt(SpawnCountPropertyName, staging.Length);
            visualEffect.SendEvent(SpawnEventName);
            PrepareBatch();
        }

        private void PrepareBatch()
        {
            float rMin = math.min(minRadius, maxRadius);
            float rMax = math.max(minRadius, maxRadius);

            staging.Clear();
            for (int i = 0; i < spawnCount; i++)
            {
                float angle = UnityEngine.Random.Range(0f, math.PI * 2f);
                float r = UnityEngine.Random.Range(rMin, rMax);
                staging.Add(new float2(math.cos(angle) * r, math.sin(angle) * r));
            }
        }

        private static bool ValidateGraphContract(VisualEffect vfx, VisualEffectAsset asset)
        {
            if (!vfx.HasGraphicsBuffer(PositionsPropertyName) || !vfx.HasInt(SpawnCountPropertyName))
            {
                Debug.LogError(
                    $"{nameof(PlayerVfxAura)} cannot use VFX asset '{asset.name}'. "
                    + $"Graph must expose GraphicsBuffer '{PositionsPropertyName}', int '{SpawnCountPropertyName}', "
                    + $"and event '{SpawnEventName}'.");
                return false;
            }

            return true;
        }
    }
}

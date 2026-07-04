using Unity.Entities;
using UnityEngine;

namespace PlayGround.System.Common
{
    // ECS Lifecycle: singleton frame timing data; created by CombatFrameClockSystem and updated once per frame.
    public struct CombatFrameClock : IComponentData
    {
        public double FrameStartTime;
        public float SmoothedFrameMs;
    }

    // ECS Lifecycle: singleton cleanup tunables; created by CombatPoolCleanupSystem when the trimmer is present.
    public struct CombatPoolCleanupConfig : IComponentData
    {
        public float BudgetMs;
        public float EmaAlpha;
        public int RetentionTarget;
        public float PoolRatioMultiplier;
        public int PerPoolDeleteCap;
        public int MaxDeletesPerFrame;
        public float SliceMs;

        public static CombatPoolCleanupConfig Default => new CombatPoolCleanupConfig
        {
            BudgetMs = 12f,
            EmaAlpha = 0.1f,
            RetentionTarget = 256,
            PoolRatioMultiplier = 4f,
            PerPoolDeleteCap = 64,
            MaxDeletesPerFrame = 256,
            SliceMs = 0.5f
        };
    }

    [UpdateInGroup(typeof(InitializationSystemGroup), OrderFirst = true)]
    public partial class CombatFrameClockSystem : SystemBase
    {
        private const float FallbackEmaAlpha = 0.1f;
        private const float MaxValidFrameMs = 1000f;

        protected override void OnCreate()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            Entity entity = EntityManager.CreateEntity(typeof(CombatFrameClock));
            EntityManager.SetComponentData(entity, new CombatFrameClock
            {
                FrameStartTime = now,
                SmoothedFrameMs = 0f
            });
        }

        protected override void OnUpdate()
        {
            double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
            CombatFrameClock clock = SystemAPI.GetSingleton<CombatFrameClock>();
            float lastFrameMs = (float)((now - clock.FrameStartTime) * 1000.0);
            float emaAlpha = SystemAPI.TryGetSingleton(out CombatPoolCleanupConfig config)
                ? config.EmaAlpha
                : FallbackEmaAlpha;

            if (lastFrameMs > 0f && lastFrameMs <= MaxValidFrameMs)
            {
                clock.SmoothedFrameMs = clock.SmoothedFrameMs <= 0f
                    ? lastFrameMs
                    : clock.SmoothedFrameMs + ((lastFrameMs - clock.SmoothedFrameMs) * emaAlpha);
            }

            clock.FrameStartTime = now;
            SystemAPI.SetSingleton(clock);
        }
    }
}

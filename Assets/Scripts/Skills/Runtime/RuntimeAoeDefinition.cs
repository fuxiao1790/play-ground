using PlayGround.Attack;
using PlayGround.System.Aoe;
using UnityEngine;

namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeAoeDefinition : RuntimeSkillDefinition
    {
        public BasicAoePrefab Prefab { get; set; }
        [Min(0.01f)] public float SizeMultiplier { get; set; } = 1f;
        public float LifetimeSeconds { get; set; }
        public float TickIntervalSeconds { get; set; }
        public int Count { get; set; } = 1;
        public bool SpawnAtAimPosition { get; set; }
        public bool DirectDamageEnabled { get; set; } = true;

        public AoeTypeDefinition CreateTypeDefinition()
        {
            var def = new AoeTypeDefinition();
            def.Configure(
                Prefab != null ? Prefab.gameObject : null,
                Prefab != null ? Prefab.Hurtbox : null,
                SizeMultiplier,
                Prefab != null ? Prefab.VisualRotationDegrees : 0f,
                0,
                Prefab != null ? Prefab.SpawnEffect : null,
                Prefab != null ? Prefab.HitEffect : null,
                Prefab != null ? Prefab.ExpireEffect : null);
            return def;
        }
    }
}

namespace PlayGround.Skills.Runtime
{
    using PlayGround.System.Combat.Audio;
    using UnityEngine;
    using Hash128 = Unity.Entities.Hash128;

    public abstract class RuntimeSkillDefinition
    {
        public int TypeId { get; set; } = -1;
        // Render identity from the unified render-resource id space; 0 means "no render".
        // Stamped onto spawn commands as RenderTypeId.
        public int RenderId { get; set; }
        public AudioClip SpawnSound { get; set; }
        // Sound identities from the AudioRoot clip id space; 0 means "no sound".
        public SkillSoundIds SoundIds { get; set; }
        // Spatial cull radius for the spawn sound; 0 means "use the AudioRoot default".
        public float SpawnSoundRadius { get; set; }
        public float Damage { get; set; }
        public float RecoveryTime { get; set; } = 0.2f;
        public float CritChance { get; set; }
        public float CritMultiplier { get; set; } = 1.5f;
        public float TriggerEnergy { get; set; } = 1f;
        public Hash128 SpawnTemplateKey { get; set; }

        // Set only when this definition is attached through a valid trigger link.
        // Combines the link's manaCostIncreased and manaCostMultiplier.
        // Feeds the initial active skill's one-time mana calculation.
        public float IncomingManaCostFactor { get; set; } = 1f;
    }
}

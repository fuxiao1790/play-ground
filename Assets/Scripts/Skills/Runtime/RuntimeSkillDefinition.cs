namespace PlayGround.Skills.Runtime
{
    public abstract class RuntimeSkillDefinition
    {
        public int TypeId { get; set; } = -1;
        public float Damage { get; set; }
        public float RecoveryTime { get; set; } = 0.2f;
    }
}

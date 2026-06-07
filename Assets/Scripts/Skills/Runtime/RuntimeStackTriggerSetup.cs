namespace PlayGround.Skills.Runtime
{
    public sealed class RuntimeStackTriggerSetup
    {
        public int DebuffStatusId { get; set; }
        public int StacksPerHit { get; set; }
        public int StackThreshold { get; set; }
        public RuntimeAoeDefinition AoeDefinition { get; set; }
    }
}

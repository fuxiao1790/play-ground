using PlayGround.Skills;

namespace PlayGround.Skills.Modifiers
{
    public readonly struct ProjectileBehaviorContext
    {
        private readonly ProjectileDefinition projectile;

        public ProjectileBehaviorContext(ProjectileDefinition projectile)
        {
            this.projectile = projectile;
        }

        public int Count
        {
            set => projectile.count = value;
        }

        public float SpreadDegrees
        {
            set => projectile.spreadDegrees = value;
        }

        public float JitterDegrees
        {
            set => projectile.jitterDegrees = value;
        }

        public float RepeatHitCooldown
        {
            set => projectile.repeatHitCooldown = value;
        }

        public bool DirectDamageEnabled
        {
            set => projectile.directDamageEnabled = value;
        }

        public void EnableTracking(float turnSpeedDegrees, float queryIntervalSeconds)
        {
            projectile.trackingEnabled = true;
            projectile.trackingTurnSpeedDegrees = turnSpeedDegrees;
            projectile.trackingQueryIntervalSeconds = queryIntervalSeconds;
        }
    }

    public readonly struct AoeBehaviorContext
    {
        private readonly AoeDefinitionBase aoe;

        public AoeBehaviorContext(AoeDefinitionBase aoe)
        {
            this.aoe = aoe;
        }

        public int EchoCount
        {
            set => aoe.echoCount = value;
        }

        public float ScatterRadius
        {
            set => aoe.scatterRadius = value;
        }

        public bool DirectDamageEnabled
        {
            set => aoe.directDamageEnabled = value;
        }
    }
}

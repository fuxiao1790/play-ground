using UnityEngine;

namespace PlayGround.Attack
{
    public sealed class LingeringAoeAttack : AoeAttack
    {
        protected override float SpawnLifetimeSeconds => config.LifetimeSeconds;
        protected override float SpawnTickIntervalSeconds => config.TickIntervalSeconds;
    }
}

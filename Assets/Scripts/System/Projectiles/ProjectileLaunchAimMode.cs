namespace PlayGround.System.Combat.Projectiles
{
    // Authored on TriggerLink; copied into RuntimeProjectileDefinition/ProjectileSpawnCommand
    // for the trigger's compiled projectile target only. None is the sentinel so existing
    // trigger assets deserialize unchanged.
    public enum ProjectileLaunchAimMode : byte
    {
        None = 0,
        NearestHostile = 1
    }
}

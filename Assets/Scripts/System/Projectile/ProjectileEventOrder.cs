namespace PlayGround.System.Projectile
{
    public static class ProjectileEventOrder
    {
        public static uint ForProjectileTarget(int projectileId, int targetId)
        {
            return ((uint)projectileId << 12) ^ (uint)(targetId & 0x0FFF);
        }
    }
}

namespace PlayGround.System.Common
{
    public enum IntervalChildKind
    {
        Projectile = 0,
        Aoe = 1
    }

    public struct OnHitSpawnRef
    {
        public IntervalChildKind Kind;
        public Unity.Entities.Hash128 TemplateKey;

        public readonly bool Enabled => !TemplateKey.Equals(default(Unity.Entities.Hash128));
    }
}

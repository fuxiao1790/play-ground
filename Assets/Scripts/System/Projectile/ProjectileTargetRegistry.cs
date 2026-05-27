using System.Collections.Generic;
namespace PlayGround.System.Projectile
{
    public sealed class ProjectileTargetRegistry
    {
        private readonly List<IProjectileTarget> targets = new();

        public IReadOnlyList<IProjectileTarget> Targets => targets;

        public void Register(IProjectileTarget target)
        {
            if (target == null || targets.Contains(target))
            {
                return;
            }

            targets.Add(target);
        }

        public void Unregister(IProjectileTarget target)
        {
            targets.Remove(target);
        }
    }
}

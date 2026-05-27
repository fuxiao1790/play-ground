using System.Collections.Generic;

namespace PlayGround.System.Aoe
{
    public sealed class AoeTargetRegistry
    {
        private readonly List<IAoeTarget> targets = new();

        public IReadOnlyList<IAoeTarget> Targets => targets;

        public void Register(IAoeTarget target)
        {
            if (target == null || targets.Contains(target))
            {
                return;
            }

            targets.Add(target);
        }

        public void Unregister(IAoeTarget target)
        {
            targets.Remove(target);
        }
    }
}

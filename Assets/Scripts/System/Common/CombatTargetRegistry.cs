using System.Collections.Generic;

namespace PlayGround.System.Common
{
    public sealed class CombatTargetRegistry<T> where T : ICombatTarget
    {
        private readonly List<T> targets = new();

        public IReadOnlyList<T> Targets => targets;

        public void Register(T target)
        {
            if (target == null || targets.Contains(target))
            {
                return;
            }

            targets.Add(target);
        }

        public void Unregister(T target)
        {
            targets.Remove(target);
        }
    }
}

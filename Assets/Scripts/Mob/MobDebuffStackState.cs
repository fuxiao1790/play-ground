using System.Collections.Generic;

namespace PlayGround.Mob
{
    public sealed class MobDebuffStackState
    {
        private readonly Dictionary<MobDebuffStatus, int> stacksByStatus = new();

        public bool AddStacks(MobDebuffStatus status, int amount, int threshold)
        {
            if (amount <= 0)
            {
                return false;
            }

            int current = GetStackCount(status) + amount;
            stacksByStatus[status] = current;
            return threshold > 0 && current >= threshold;
        }

        public int GetStackCount(MobDebuffStatus status)
        {
            return stacksByStatus.TryGetValue(status, out int count) ? count : 0;
        }

        public void ClearStacks(MobDebuffStatus status)
        {
            stacksByStatus.Remove(status);
        }
    }
}

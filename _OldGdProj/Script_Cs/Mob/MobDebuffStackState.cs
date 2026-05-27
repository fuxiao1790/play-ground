namespace PlayGround.Mob;

public sealed class MobDebuffStackState
{
    private const int MaximumStatusCount = 50;
    private readonly int[] _stackCounts = new int[MaximumStatusCount];

    public int GetStackCount(MobDebuffStatus status)
    {
        int index = StatusIndex(status);
        return index >= 0 ? _stackCounts[index] : 0;
    }

    public bool AddStacks(MobDebuffStatus status, int amount, int threshold)
    {
        int index = StatusIndex(status);
        if (index < 0 || amount <= 0 || threshold <= 0)
        {
            return false;
        }

        int newCount = _stackCounts[index] + amount;
        if (newCount >= threshold)
        {
            _stackCounts[index] = 0;
            return true;
        }

        _stackCounts[index] = newCount;
        return false;
    }

    public void ClearStacks(MobDebuffStatus status)
    {
        int index = StatusIndex(status);
        if (index >= 0)
        {
            _stackCounts[index] = 0;
        }
    }

    private static int StatusIndex(MobDebuffStatus status)
    {
        int index = (int)status;
        return index >= 0 && index < MaximumStatusCount ? index : -1;
    }
}

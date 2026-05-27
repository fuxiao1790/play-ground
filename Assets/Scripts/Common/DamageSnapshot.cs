namespace PlayGround.Common
{
    public readonly struct DamageSnapshot
    {
        public DamageSnapshot(float amount)
        {
            Amount = amount;
        }

        public float Amount { get; }
    }
}

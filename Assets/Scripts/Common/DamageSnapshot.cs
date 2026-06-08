namespace PlayGround.Common
{
    public readonly struct DamageSnapshot
    {
        public DamageSnapshot(float amount, bool isCrit = false)
        {
            Amount = amount;
            IsCrit = isCrit;
        }

        public float Amount { get; }
        public bool IsCrit { get; }
    }
}

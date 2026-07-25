using UnityEngine;

namespace PlayGround.Common.Stats
{
    [CreateAssetMenu(menuName = "PlayGround/Units/Unit Stat Sheet")]
    public sealed class UnitStatSheet : ScriptableObject
    {
        [Header("Vitals")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float maxMana = 100f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Offense")]
        [SerializeField, Min(0f), Tooltip("Authored as percent points. 15 means +15% attack/cast rate.")]
        private float increasedRatePercent = 0f;
        [SerializeField] private float damageMultiplier = 1f;
        [SerializeField, Range(0f, 1f)] private float critChance = 0f;
        [SerializeField] private float critMultiplier = 1.5f;
        [SerializeField] private float areaSizeMultiplier = 1f;

        public float MaxHealth => Mathf.Max(1f, maxHealth);
        public float MaxMana => Mathf.Max(0f, maxMana);
        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public float IncreasedRatePercent => Mathf.Max(0f, increasedRatePercent) * 0.01f;
        public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);
        public float CritChance => Mathf.Clamp01(critChance);
        public float CritMultiplier => Mathf.Max(1f, critMultiplier);
        public float AreaSizeMultiplier => Mathf.Max(0f, areaSizeMultiplier);

        internal void SetRuntimeValues(float maxHealth, float moveSpeed)
        {
            this.maxHealth = maxHealth;
            this.moveSpeed = moveSpeed;
        }
    }
}

using UnityEngine;

namespace PlayGround.Spawn
{
    [CreateAssetMenu(menuName = "Play Ground/Spawn/Spawn Config")]
    public sealed class SpawnConfig : ScriptableObject
    {
        [SerializeField] private MobSpawnPool pool;
        [SerializeField, Min(0.05f)] private float spawnInterval = 2f;
        [SerializeField] private bool autostart = true;
        [SerializeField, Min(0f)] private float spawnRadius = 16f;
        [SerializeField, Min(0f)] private float mobClearanceRadius = 0.5f;
        [SerializeField, Min(0)] private int maxLocalMobs;

        public MobSpawnPool Pool => pool;
        public float SpawnInterval => spawnInterval;
        public bool Autostart => autostart;
        public float SpawnRadius => spawnRadius;
        public float MobClearanceRadius => mobClearanceRadius;
        public int MaxLocalMobs => maxLocalMobs;
    }
}

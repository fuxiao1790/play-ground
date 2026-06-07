using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/Child Spawn", fileName = "NewChildSpawnTrigger")]
    public sealed class ChildSpawnTrigger : TriggerLink
    {
        [Min(0.01f)] public float intervalSeconds = 0.5f;
        [Min(1)] public int spawnCount = 1;
        [Range(0f, 180f)] public float sideSpreadDegrees = 30f;
    }
}

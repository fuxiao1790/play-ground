using System;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Spawn
{
    [CreateAssetMenu(menuName = "Play Ground/Spawn/Mob Spawn Pool")]
    public sealed class MobSpawnPool : ScriptableObject
    {
        [SerializeField] private MobSpawnEntry[] entries = Array.Empty<MobSpawnEntry>();

        public bool HasEntries => entries != null && entries.Length > 0;

        public void Configure(MobRoot[] prefabs)
        {
            if (prefabs == null)
            {
                entries = Array.Empty<MobSpawnEntry>();
                return;
            }

            entries = new MobSpawnEntry[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                entries[i] = new MobSpawnEntry { prefab = prefabs[i], weight = 1 };
            }
        }

        public MobRoot ChoosePrefab(global::System.Random random)
        {
            if (!HasEntries)
            {
                return null;
            }

            int totalWeight = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].prefab != null)
                {
                    totalWeight += Mathf.Max(1, entries[i].weight);
                }
            }

            if (totalWeight <= 0)
            {
                return null;
            }

            int roll = random.Next(0, totalWeight);
            int cursor = 0;
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].prefab == null)
                {
                    continue;
                }

                cursor += Mathf.Max(1, entries[i].weight);
                if (roll < cursor)
                {
                    return entries[i].prefab;
                }
            }

            return null;
        }

        [Serializable]
        private struct MobSpawnEntry
        {
            public MobRoot prefab;
            [Min(1)] public int weight;
        }
    }
}

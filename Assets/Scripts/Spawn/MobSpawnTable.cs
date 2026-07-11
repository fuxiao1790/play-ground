using System;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Spawn
{
    [CreateAssetMenu(menuName = "Play Ground/Spawn/Mob Spawn Table")]
    public sealed class MobSpawnTable : ScriptableObject
    {
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public bool HasEntries => entries != null && entries.Length > 0;
        public int EntryCount => entries?.Length ?? 0;

        public void Configure(MobRoot[] prefabs)
        {
            if (prefabs == null)
            {
                entries = Array.Empty<Entry>();
                return;
            }

            entries = new Entry[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                entries[i] = new Entry { prefab = prefabs[i], weight = 1 };
            }
        }

        public MobRoot GetPrefabAt(int index)
        {
            if (entries == null || index < 0 || index >= entries.Length)
            {
                return null;
            }

            return entries[index].prefab;
        }

        public MobRoot ChoosePrefab(global::System.Random random)
        {
            if (!HasEntries)
            {
                return null;
            }

            random ??= new global::System.Random();

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
        public struct Entry
        {
            public MobRoot prefab;
            [Min(1)] public int weight;
        }
    }
}

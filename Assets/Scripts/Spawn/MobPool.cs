using System.Collections.Generic;
using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Spawn
{
    public sealed class MobPool
    {
        private readonly Transform poolRoot;
        private readonly Dictionary<MobRoot, Stack<MobRoot>> free = new();

        public MobPool(Transform poolRoot)
        {
            this.poolRoot = poolRoot;
        }

        public MobRoot Rent(MobRoot prefab, Vector2 position)
        {
            if (prefab == null)
            {
                return null;
            }

            Stack<MobRoot> stack = GetStack(prefab);
            MobRoot mob = stack.Count > 0 ? stack.Pop() : InstantiateMob(prefab);
            Transform mobTransform = mob.transform;
            mobTransform.SetParent(null, true);
            mobTransform.position = position;
            return mob;
        }

        public void Return(MobRoot mob)
        {
            if (mob == null)
            {
                return;
            }

            PooledMob marker = mob.GetComponent<PooledMob>();
            MobRoot origin = marker != null ? marker.Origin : null;
            if (origin == null)
            {
                return;
            }

            mob.gameObject.SetActive(false);
            mob.transform.SetParent(poolRoot, true);
            GetStack(origin).Push(mob);
        }

        public void Prewarm(MobSpawnTable table, int count)
        {
            if (table == null || count <= 0 || !table.HasEntries)
            {
                return;
            }

            int entryCount = table.EntryCount;
            if (entryCount <= 0)
            {
                return;
            }

            int created = 0;
            int index = 0;
            while (created < count)
            {
                MobRoot prefab = table.GetPrefabAt(index % entryCount);
                index++;
                if (prefab == null)
                {
                    if (index >= entryCount && created == 0)
                    {
                        return;
                    }

                    continue;
                }

                MobRoot mob = InstantiateMob(prefab);
                GetStack(prefab).Push(mob);
                created++;
            }
        }

        private MobRoot InstantiateMob(MobRoot prefab)
        {
            MobRoot mob = Object.Instantiate(prefab, poolRoot);
            mob.gameObject.SetActive(false);
            PooledMob marker = mob.GetComponent<PooledMob>();
            if (marker == null)
            {
                marker = mob.gameObject.AddComponent<PooledMob>();
            }

            marker.Origin = prefab;
            return mob;
        }

        private Stack<MobRoot> GetStack(MobRoot prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<MobRoot> stack))
            {
                stack = new Stack<MobRoot>();
                free.Add(prefab, stack);
            }

            return stack;
        }

    }

    public sealed class PooledMob : MonoBehaviour
    {
        public MobRoot Origin { get; set; }
    }
}

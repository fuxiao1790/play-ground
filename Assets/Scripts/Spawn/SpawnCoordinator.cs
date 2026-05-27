using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Spawn
{
    public class SpawnCoordinator : MonoBehaviour
    {
        public virtual bool CanSpawn(SpawnPoint spawnPoint)
        {
            return true;
        }

        public virtual void OnSpawned(SpawnPoint spawnPoint, MobRoot mob)
        {
        }

        public virtual void OnDespawned(MobRoot mob)
        {
        }
    }
}

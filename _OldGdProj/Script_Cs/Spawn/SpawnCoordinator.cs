using Godot;

namespace PlayGround.Spawn;

[GlobalClass]
public partial class SpawnCoordinator : Node
{
    public virtual bool CanSpawn(Node? spawnPoint)
    {
        return true;
    }

    public virtual bool can_spawn(Node? spawnPoint) => CanSpawn(spawnPoint);

    public virtual void OnSpawned(SpawnPoint spawnPoint, Node mob)
    {
    }

    public virtual void on_spawned(SpawnPoint spawnPoint, Node mob) => OnSpawned(spawnPoint, mob);

    public virtual void OnDespawned(Node mob)
    {
    }

    public virtual void on_despawned(Node mob) => OnDespawned(mob);
}

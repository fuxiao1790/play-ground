using Godot;
using PlayGround.Common;
using System.Collections.Generic;
using MobRoot = PlayGround.Mob.Mob;

namespace PlayGround.Spawn;

[GlobalClass]
public partial class MobSpawnerRoot : Node
{
    [Export] public Godot.Collections.Array<PackedScene> mob_scenes = new();
    [Export] public int max_mobs = 20;
    [Export] public SpawnCoordinator? coordinator;

    private readonly List<Node> _spawnedMobs = new();
    private readonly HashSet<Node> _softDeadMobs = new();
    private readonly RandomNumberGenerator _rng = new();
    private bool _active = true;

    public override void _Ready()
    {
        _rng.Randomize();
    }

    public bool CanSpawn(Node? spawnPoint)
    {
        Cleanup();

        if (!_active)
        {
            return false;
        }

        if (ActiveMobCount() >= max_mobs)
        {
            return false;
        }

        if (coordinator != null && !coordinator.can_spawn(spawnPoint))
        {
            return false;
        }

        return true;
    }

    public bool can_spawn(Node? spawnPoint) => CanSpawn(spawnPoint);

    public int ActiveMobCount()
    {
        Cleanup();
        int count = 0;
        foreach (Node mob in _spawnedMobs)
        {
            if (DamageableState.IsAlive(mob))
            {
                count++;
            }
        }
        return count;
    }

    public int active_mob_count() => ActiveMobCount();

    public Node? RequestSpawn(SpawnPoint spawnPoint)
    {
        if (!CanSpawn(spawnPoint))
        {
            return null;
        }

        Godot.Collections.Array<PackedScene> scenes = spawnPoint.MobScenesOrFallback(mob_scenes);
        if (scenes.Count == 0)
        {
            GD.PushError(
                $"No mob scenes available for SpawnPoint '{spawnPoint.Name}'. " +
                "Assign mob_scenes on the point, on its SpawnConfig, or on MobSpawnerRoot as a fallback.");
            return null;
        }

        PackedScene scene = scenes[_rng.RandiRange(0, scenes.Count - 1)];
        Node mob = scene.Instantiate();
        Node parent = GetTree().CurrentScene ?? this;
        parent.AddChild(mob);

        if (mob is Node2D mob2D)
        {
            Vector2 position = spawnPoint.FindSpawnPosition();
            if (position == Vector2.Inf)
            {
                mob.QueueFree();
                return null;
            }
            mob2D.GlobalPosition = position;
        }

        RegisterMob(spawnPoint, mob);
        spawnPoint.RegisterMob(mob);
        return mob;
    }

    public Node? request_spawn(SpawnPoint spawnPoint) => RequestSpawn(spawnPoint);

    public void DespawnAll()
    {
        foreach (Node mob in _spawnedMobs.ToArray())
        {
            if (GodotObject.IsInstanceValid(mob))
            {
                mob.QueueFree();
            }
        }
        _spawnedMobs.Clear();
        _softDeadMobs.Clear();
    }

    public void despawn_all() => DespawnAll();

    public void Stop()
    {
        _active = false;
    }

    public void stop() => Stop();

    public void Start()
    {
        _active = true;
    }

    public void start() => Start();

    private void RegisterMob(SpawnPoint spawnPoint, Node mob)
    {
        _spawnedMobs.Add(mob);
        if (mob is MobRoot softDeathMob)
        {
            softDeathMob.SoftDied += OnMobSoftDied;
        }
        mob.TreeExiting += () => OnMobRemoved(mob);
        coordinator?.on_spawned(spawnPoint, mob);
    }

    private void OnMobRemoved(Node mob)
    {
        if (mob is MobRoot softDeathMob)
        {
            softDeathMob.SoftDied -= OnMobSoftDied;
        }

        bool wasTracked = _spawnedMobs.Remove(mob);
        bool wasSoftDead = _softDeadMobs.Remove(mob);
        if (wasTracked && !wasSoftDead)
        {
            coordinator?.on_despawned(mob);
        }
    }

    private void OnMobSoftDied(MobRoot mob)
    {
        if (!_spawnedMobs.Contains(mob) || !_softDeadMobs.Add(mob))
        {
            return;
        }

        coordinator?.on_despawned(mob);
    }

    private void Cleanup()
    {
        _spawnedMobs.RemoveAll(mob => !GodotObject.IsInstanceValid(mob));
        _softDeadMobs.RemoveWhere(mob => !GodotObject.IsInstanceValid(mob));
    }
}

using Godot;
using PlayGround.Common;
using System.Collections.Generic;
using MobRoot = PlayGround.Mob.Mob;

namespace PlayGround.Spawn;

[GlobalClass]
public partial class SpawnPoint : Node2D
{
    [Export] public Godot.Collections.Array<PackedScene> mob_scenes = new();
    [Export] public SpawnConfig? config;
    [Export] public float spawn_interval = 2.0f;
    [Export] public bool autostart = true;
    [Export] public float spawn_radius = 16.0f;
    [Export] public Vector2 spawn_origin = Vector2.Zero;
    [Export] public bool debug_draw = true;

    public MobSpawnerRoot? root;

    private Timer? _timer;
    private readonly List<Node> _localMobs = new();
    private bool _radiiComputed;
    private float _computedSpawnRadius;
    private float _mobRadius;

    public override void _Ready()
    {
        root = GetParent() as MobSpawnerRoot;

        if (root == null)
        {
            FailConfiguration($"SpawnPoint must be a direct child of MobSpawnerRoot: {Name}");
            return;
        }

        _timer = new Timer
        {
            WaitTime = spawn_interval,
            Autostart = autostart,
        };
        _timer.Timeout += OnTimeout;
        AddChild(_timer);

        ComputeRadii();
        QueueRedraw();
    }

    public void RegisterMob(Node mob)
    {
        _localMobs.Add(mob);
        if (mob is MobRoot softDeathMob)
        {
            softDeathMob.SoftDied += OnLocalMobSoftDied;
        }
        mob.TreeExiting += () => OnLocalMobRemoved(mob);
    }

    public void register_mob(Node mob) => RegisterMob(mob);

    public Vector2 FindSpawnPosition()
    {
        CleanupLocal();
        Vector2 origin = spawn_origin == Vector2.Zero ? GlobalPosition : spawn_origin;
        if (!_radiiComputed)
        {
            return origin;
        }

        return FindFreePosition(origin, _computedSpawnRadius, _mobRadius);
    }

    public Vector2 find_spawn_position() => FindSpawnPosition();

    public Godot.Collections.Array<PackedScene> MobScenesOrFallback(Godot.Collections.Array<PackedScene> fallback)
    {
        if (mob_scenes.Count > 0)
        {
            return mob_scenes;
        }

        if (config != null && config.mob_scenes.Count > 0)
        {
            return config.mob_scenes;
        }

        return fallback;
    }

    public override void _Draw()
    {
        if (!debug_draw || (!Engine.IsEditorHint() && !OS.IsDebugBuild()))
        {
            return;
        }

        Vector2 origin = ToLocal(spawn_origin == Vector2.Zero ? GlobalPosition : spawn_origin);
        DrawArc(origin, _computedSpawnRadius, 0.0f, Mathf.Tau, 32, new Color(1.0f, 0.8f, 0.0f, 0.8f), 1.0f);

        if (_mobRadius > 0.0f)
        {
            DrawArc(origin, _mobRadius, 0.0f, Mathf.Tau, 32, new Color(1.0f, 0.3f, 0.3f, 0.6f), 1.0f);
        }

        DrawCircle(origin, 2.0f, new Color(1.0f, 1.0f, 1.0f, 0.9f));
    }

    private void OnTimeout()
    {
        root?.RequestSpawn(this);
    }

    private void OnLocalMobRemoved(Node mob)
    {
        if (mob is MobRoot softDeathMob)
        {
            softDeathMob.SoftDied -= OnLocalMobSoftDied;
        }
        _localMobs.Remove(mob);
    }

    private void OnLocalMobSoftDied(MobRoot mob)
    {
        _localMobs.Remove(mob);
    }

    private void CleanupLocal()
    {
        _localMobs.RemoveAll(mob => !DamageableState.IsAlive(mob));
    }

    private static Vector2 RandomOffset(float radius)
    {
        float angle = GD.Randf() * Mathf.Tau;
        float distance = GD.Randf() * radius;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    private void ComputeRadii()
    {
        _mobRadius = 0.0f;
        foreach (PackedScene packed in MobScenesOrFallback(root?.mob_scenes ?? new Godot.Collections.Array<PackedScene>()))
        {
            Node instance = packed.Instantiate();
            _mobRadius = Mathf.Max(_mobRadius, MobVisualRadius(instance));
            instance.Free();
        }

        _computedSpawnRadius = Mathf.Max(spawn_radius, _mobRadius);
        _radiiComputed = true;
    }

    private static float MobVisualRadius(Node instance)
    {
        if (instance is not Node2D)
        {
            return 0.0f;
        }

        foreach (Node child in instance.GetChildren())
        {
            if (child is Sprite2D sprite && sprite.Texture != null)
            {
                return sprite.Texture.GetSize().Length() / 2.0f;
            }

            if (child is AnimatedSprite2D animatedSprite && animatedSprite.SpriteFrames != null)
            {
                Texture2D? texture = FirstFrameTexture(animatedSprite.SpriteFrames);
                if (texture != null)
                {
                    return texture.GetSize().Length() / 2.0f;
                }
            }
        }

        return 0.0f;
    }

    private static Texture2D? FirstFrameTexture(SpriteFrames spriteFrames)
    {
        string[] animationNames = spriteFrames.GetAnimationNames();
        if (animationNames.Length == 0)
        {
            return null;
        }

        StringName animation = animationNames[0];
        return spriteFrames.GetFrameCount(animation) > 0
            ? spriteFrames.GetFrameTexture(animation, 0)
            : null;
    }

    private Vector2 FindFreePosition(Vector2 origin, float searchRadius, float mobRadius, int maxAttempts = 10)
    {
        float minSeparation = mobRadius * 2.0f;
        for (int i = 0; i < maxAttempts; i++)
        {
            Vector2 candidate = origin + RandomOffset(searchRadius);
            bool occupied = false;
            foreach (Node mob in _localMobs)
            {
                if (DamageableState.IsAlive(mob) && mob is Node2D mob2D)
                {
                    if (mob2D.GlobalPosition.DistanceTo(candidate) < minSeparation)
                    {
                        occupied = true;
                        break;
                    }
                }
            }

            if (!occupied)
            {
                return candidate;
            }
        }

        return Vector2.Inf;
    }

    private void FailConfiguration(string message)
    {
        GD.PushError(message);
        SetProcess(false);
        SetPhysicsProcess(false);
    }
}

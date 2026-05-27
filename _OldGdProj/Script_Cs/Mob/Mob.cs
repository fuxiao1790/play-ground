using Godot;
using PlayGround.Common;
using System;
using System.Collections.Generic;

namespace PlayGround.Mob;

public partial class Mob : RigidBody2D, IDamageable
{
    [Export] public float speed = 40.0f;
    [Export] public int health = 3;
    [Export] public Godot.Collections.Array<MobBehaviour> behaviours = new();
    [Export] public Godot.Collections.Array<MobTrigger> triggers = new();
    [Export] public Godot.Collections.Dictionary trigger_behaviour_map = new();
    [Export] public bool debug_draw;
    [Export] public bool projectile_attack_enabled;
    [Export] public NodePath projectile_system_path = new("../MobProjectileManager");
    [Export] public StringName actor_group = null!;
    [Export] public StringName projectile_target_group = null!;
    [Export] public PackedScene? projectile_attack_scene;
    [Export] public float projectile_attack_cooldown = 1.2f;
    [Export] public float projectile_attack_range = 130.0f;
    [Export] public float projectile_attack_spawn_offset = 12.0f;
    [Export] public float projectile_attack_speed = 220.0f;
    [Export] public float projectile_attack_lifetime = 1.8f;
    [Export] public int projectile_attack_damage = 1;
    [Export] public bool projectile_attack_tracking_enabled;
    [Export] public float projectile_attack_tracking_range = 0.0f;
    [Export] public float projectile_attack_tracking_turn_speed_degrees = 0.0f;

    private static readonly StringName DefaultTriggerKey = "on_spawn";
    private const int DebugFontSize = 10;
    private static readonly Vector2 DebugOffset = new(-42.0f, -58.0f);
    private const float DebugLineHeight = 11.0f;
    private static readonly Vector2 DebugPadding = new(4.0f, 3.0f);
    private static readonly Vector2 DebugHealthBarSize = new(48.0f, 4.0f);
    private static readonly Color[] DebugColours =
    {
        new(0.55f, 0.85f, 1.00f),
        new(0.40f, 1.00f, 0.55f),
        new(1.00f, 0.35f, 0.35f),
    };

    private AnimatedSprite2D _sprite = null!;
    private StateMachineCore _animStateMachine = null!;
    private MobAnimator _animator = null!;
    private StateMachineCore _behaviourStateMachine = null!;
    private MobEventQueue _eventQueue = null!;
    private MobBlackboard _blackboard = null!;
    private MobStateDriver _stateDriver = null!;
    private MobBehaviourSelector _behaviourSelector = null!;
    private readonly MobDebuffStackState _debuffStacks = new();
    private readonly List<MobTrigger> _runtimeTriggers = new();
    private List<string> _debugWidgetLinesCache = new();
    private Color _debugWidgetColour = Colors.White;
    private float _debugHealthFillRatio = 1.0f;
    private bool _isAlive = true;
    private bool _softDeathNotified;
    private MobProjectileAttack? _projectileAttack;

    public event Action<Mob>? SoftDied;

    public override void _Ready()
    {
        RequireConfiguredGroup(actor_group, nameof(actor_group));
        RequireConfiguredGroup(projectile_target_group, nameof(projectile_target_group));
        AddConfiguredGroup(projectile_target_group);
        AddConfiguredGroup(actor_group);
        SetupAnimationSystem();
        SetupBehaviourSystem();
        SetupProjectileAttack();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!IsAlive())
        {
            return;
        }

        _blackboard.BeginFrame(DefaultTriggerKey);
        _blackboard.health = health;
        _blackboard.behaviour_state = _behaviourStateMachine.CurrentState;

        foreach (MobTrigger trigger in _runtimeTriggers)
        {
            trigger.Update(delta, this, _blackboard, _eventQueue);
        }

        _eventQueue.PushType(MobEvent.EventType.Tick, this);
        _stateDriver.Update(_eventQueue.Drain());
        _blackboard.behaviour_state = _behaviourStateMachine.CurrentState;
        _projectileAttack?.Update(delta, _blackboard.target);

        LinearVelocity = _behaviourSelector.Update(delta, this, _blackboard, (MobBehaviourState)_behaviourStateMachine.CurrentState);
    }

    public override void _Process(double delta)
    {
        RefreshDebugDisplay();
    }

    public override void _Draw()
    {
        if (!debug_draw || _debugWidgetLinesCache.Count == 0)
        {
            return;
        }

        DrawDebugWidget();
    }

    public void take_damage(int amount = 1)
    {
        TakeDamage(amount);
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || !IsAlive())
        {
            return;
        }

        health -= amount;
        if (health <= 0)
        {
            health = 0;
            SoftDie();
            _eventQueue.PushType(MobEvent.EventType.Died, this);
            return;
        }

        _eventQueue.PushType(MobEvent.EventType.Damaged, this, amount);
    }

    public bool IsAlive() => _isAlive && health > 0;
    public bool is_alive() => IsAlive();

    public bool AddDebuffStacks(MobDebuffStatus status, int amount, int threshold)
    {
        return _debuffStacks.AddStacks(status, amount, threshold);
    }

    public int GetDebuffStackCount(MobDebuffStatus status)
    {
        return _debuffStacks.GetStackCount(status);
    }

    public void ClearDebuffStacks(MobDebuffStatus status)
    {
        _debuffStacks.ClearStacks(status);
    }

    public void SoftDie()
    {
        if (_softDeathNotified)
        {
            return;
        }

        _isAlive = false;
        health = 0;
        LinearVelocity = Vector2.Zero;
        RemoveConfiguredGroup(projectile_target_group);
        RemoveConfiguredGroup(actor_group);
        Hide();
        DisableCollisionNode(this);
        DisableCollisionNode(GetNodeOrNull<Area2D>("Hurtbox"));
        SetPhysicsProcess(false);
        SetProcess(false);
        SoftDied?.Invoke(this);
        _softDeathNotified = true;
    }

    public void soft_die() => SoftDie();

    private void DrawDebugWidget()
    {
        Font font = ThemeDB.FallbackFont;
        float width = 0.0f;
        foreach (string line in _debugWidgetLinesCache)
        {
            Vector2 lineSize = font.GetStringSize(line, HorizontalAlignment.Left, -1.0f, DebugFontSize);
            width = Mathf.Max(width, lineSize.X);
        }
        width = Mathf.Max(width, DebugHealthBarSize.X);

        float contentHeight = _debugWidgetLinesCache.Count * DebugLineHeight + DebugHealthBarSize.Y;
        var rect = new Rect2(
            DebugOffset - DebugPadding,
            new Vector2(width, contentHeight) + DebugPadding * 2.0f);
        DrawRect(rect, new Color(0.0f, 0.0f, 0.0f, 0.68f));
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 1.0f)), _debugWidgetColour);

        for (int i = 0; i < _debugWidgetLinesCache.Count; i++)
        {
            Color lineColour = i == 0 ? _debugWidgetColour : Colors.White;
            DrawString(
                font,
                DebugOffset + new Vector2(0.0f, (i + 1) * DebugLineHeight - 2.0f),
                _debugWidgetLinesCache[i],
                HorizontalAlignment.Left,
                -1.0f,
                DebugFontSize,
                lineColour);
        }

        DrawDebugHealthBar(DebugOffset + new Vector2(0.0f, _debugWidgetLinesCache.Count * DebugLineHeight + 1.0f));
    }

    private void RefreshDebugDisplay()
    {
        if (!debug_draw)
        {
            if (_debugWidgetLinesCache.Count > 0)
            {
                _debugWidgetLinesCache = new List<string>();
                QueueRedraw();
            }
            return;
        }

        RefreshDebugWidgetCache();
        QueueRedraw();
    }

    private void RefreshDebugWidgetCache()
    {
        int animState = _animStateMachine.CurrentState;
        string animLabel = DebugAnimStateName(animState);
        int behaviourState = _behaviourStateMachine.CurrentState;
        string behaviourLabel = DebugBehaviourStateName(behaviourState);
        _debugWidgetColour = animState >= 0 && animState < DebugColours.Length ? DebugColours[animState] : Colors.White;
        _debugWidgetLinesCache = DebugWidgetLines(behaviourLabel, animLabel);

        int total = Mathf.Max(_blackboard.max_health, health);
        _debugHealthFillRatio = total > 0 ? Mathf.Clamp((float)health / total, 0.0f, 1.0f) : 0.0f;
    }

    private List<string> DebugWidgetLines(string behaviourLabel, string animLabel)
    {
        string behaviourKey = _blackboard.active_behaviour_key.ToString();
        if (behaviourKey == string.Empty)
        {
            behaviourKey = "none";
        }

        string targetLabel = "none";
        if (_blackboard.HasValidTarget())
        {
            float targetDistance = GlobalPosition.DistanceTo(_blackboard.target!.GlobalPosition);
            string visibleLabel = _blackboard.target_visible ? "seen" : "held";
            targetLabel = $"{_blackboard.target.Name} {visibleLabel} {targetDistance:0}";
        }

        return new List<string>
        {
            $"{behaviourLabel}/{animLabel}",
            $"trig: {_blackboard.active_trigger_key}",
            $"beh: {behaviourKey}",
            $"target: {targetLabel}",
            $"hp: {health}/{_blackboard.max_health}",
        };
    }

    private void DrawDebugHealthBar(Vector2 origin)
    {
        var barRect = new Rect2(origin, DebugHealthBarSize);
        var fillRect = new Rect2(origin, new Vector2(DebugHealthBarSize.X * _debugHealthFillRatio, DebugHealthBarSize.Y));
        DrawRect(barRect, new Color(0.20f, 0.25f, 0.22f));
        DrawRect(fillRect, new Color(0.20f, 0.90f, 0.30f));
    }

    private void SetupAnimationSystem()
    {
        _sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        _animator = new MobAnimator(_sprite);
        _animStateMachine = new StateMachineCore();
        _animStateMachine.StateChanged += OnAnimationStateChanged;
        _animStateMachine.Init((int)MobAnimator.State.Idle);
    }

    private void UpdateAnimationState(MobAnimator.State desiredState)
    {
        _animStateMachine.TransitionTo((int)desiredState);
    }

    private void SetupBehaviourSystem()
    {
        _blackboard = new MobBlackboard
        {
            health = health,
            max_health = Mathf.Max(health, 1),
        };

        _eventQueue = new MobEventQueue();
        _behaviourSelector = new MobBehaviourSelector();
        bool registeredBehaviours = RegisterBehaviours();
        bool registeredTriggers = RegisterTriggers();
        bool validComposition = ValidateBehaviourComposition();
        if (!registeredBehaviours || !registeredTriggers || !validComposition)
        {
            throw new InvalidOperationException(
                $"Mob '{Name}' has invalid behaviour composition. See logged errors for details.");
        }

        _behaviourSelector.Configure(trigger_behaviour_map);
        _behaviourStateMachine = new StateMachineCore();
        _behaviourStateMachine.StateChanged += OnBehaviourStateChanged;
        _behaviourStateMachine.Init((int)MobBehaviourState.Idle);
        _blackboard.behaviour_state = _behaviourStateMachine.CurrentState;
        _stateDriver = new MobStateDriver(_behaviourStateMachine, _blackboard);
    }

    private void SetupProjectileAttack()
    {
        if (!projectile_attack_enabled)
        {
            return;
        }

        if (projectile_attack_scene == null)
        {
            throw new InvalidOperationException(
                $"Mob '{Name}' has projectile_attack_enabled but no projectile_attack_scene assigned.");
        }

        _projectileAttack = new MobProjectileAttack(
            this,
            projectile_attack_scene,
            projectile_system_path,
            projectile_attack_cooldown,
            projectile_attack_range,
            projectile_attack_spawn_offset,
            projectile_attack_speed,
            projectile_attack_lifetime,
            projectile_attack_damage,
            new ProjectileTrackingConfig(
                projectile_attack_tracking_enabled,
                projectile_attack_tracking_range,
                projectile_attack_tracking_turn_speed_degrees));
        _projectileAttack.Setup();
    }

    private bool RegisterBehaviours()
    {
        bool isValid = true;
        for (int index = 0; index < behaviours.Count; index++)
        {
            MobBehaviour? behaviour = behaviours[index];
            if (behaviour == null)
            {
                FailBehaviourComposition($"Mob '{Name}' behaviours[{index}] must be assigned.");
                isValid = false;
                continue;
            }
            if (behaviour.Key.ToString() == string.Empty)
            {
                FailBehaviourComposition($"Mob '{Name}' behaviours[{index}] must define a non-empty key.");
                isValid = false;
                continue;
            }
            _behaviourSelector.Register(behaviour);
        }
        return isValid;
    }

    private bool RegisterTriggers()
    {
        bool isValid = true;
        for (int index = 0; index < triggers.Count; index++)
        {
            MobTrigger? trigger = triggers[index];
            if (trigger == null)
            {
                FailBehaviourComposition($"Mob '{Name}' triggers[{index}] must be assigned.");
                isValid = false;
                continue;
            }
            _runtimeTriggers.Add(trigger);
        }
        return isValid;
    }

    private bool ValidateBehaviourComposition()
    {
        bool isValid = true;
        string prefix = $"Mob '{Name}' behaviour composition error: ";
        if (behaviours.Count == 0)
        {
            FailBehaviourComposition(prefix + "behaviours must not be empty.");
            isValid = false;
        }
        if (triggers.Count == 0)
        {
            FailBehaviourComposition(prefix + "triggers must not be empty.");
            isValid = false;
        }
        if (trigger_behaviour_map.Count == 0)
        {
            FailBehaviourComposition(prefix + "trigger_behaviour_map must not be empty.");
            isValid = false;
        }
        if (!HasTriggerMapping(DefaultTriggerKey))
        {
            FailBehaviourComposition(prefix + $"trigger_behaviour_map must define '{DefaultTriggerKey}'.");
            isValid = false;
        }

        foreach (Variant triggerKey in trigger_behaviour_map.Keys)
        {
            StringName behaviourKey = new(trigger_behaviour_map[triggerKey].AsString());
            if (behaviourKey.ToString() == string.Empty)
            {
                FailBehaviourComposition(prefix + $"trigger '{triggerKey}' maps to an empty behaviour key.");
                isValid = false;
            }
            if (!_behaviourSelector.HasBehaviour(behaviourKey))
            {
                FailBehaviourComposition(prefix + $"trigger '{triggerKey}' maps to missing behaviour '{behaviourKey}'.");
                isValid = false;
            }
        }
        return isValid;
    }

    private bool HasTriggerMapping(StringName triggerKey)
    {
        return trigger_behaviour_map.ContainsKey(triggerKey) || trigger_behaviour_map.ContainsKey(triggerKey.ToString());
    }

    private static void FailBehaviourComposition(string message)
    {
        GD.PushError(message);
        GD.PushWarning(message);
    }

    private void OnAnimationStateChanged(int fromState, int toState)
    {
        int priority = toState == (int)MobAnimator.State.Hurt ? MobAnimator.PriorityHurt : MobAnimator.PriorityLocomotion;
        _animator.Request((MobAnimator.State)toState, priority);
    }

    private void OnBehaviourStateChanged(int fromState, int toState)
    {
        switch ((MobBehaviourState)toState)
        {
            case MobBehaviourState.Idle:
                UpdateAnimationState(MobAnimator.State.Idle);
                break;
            case MobBehaviourState.Wander:
            case MobBehaviourState.Chase:
                UpdateAnimationState(MobAnimator.State.Walk);
                break;
            case MobBehaviourState.Hurt:
                UpdateAnimationState(MobAnimator.State.Hurt);
                break;
            case MobBehaviourState.Dead:
                SoftDie();
                break;
        }
    }

    private static void DisableCollisionNode(Node? node)
    {
        if (node == null)
        {
            return;
        }

        if (node is CollisionObject2D collisionObject)
        {
            collisionObject.CollisionLayer = 0;
            collisionObject.CollisionMask = 0;
        }

        if (node is Area2D area)
        {
            area.Monitoring = false;
            area.Monitorable = false;
        }

        foreach (Node child in node.GetChildren())
        {
            if (child is CollisionShape2D shape)
            {
                shape.Disabled = true;
            }
        }
    }

    private void AddConfiguredGroup(StringName groupName)
    {
        AddToGroup(groupName);
    }

    private void RemoveConfiguredGroup(StringName groupName)
    {
        RemoveFromGroup(groupName);
    }

    private static void RequireConfiguredGroup(StringName groupName, string propertyName)
    {
        if (groupName.ToString() == string.Empty)
        {
            throw new InvalidOperationException(
                $"Mob requires '{propertyName}' to be configured in the scene.");
        }
    }

    private static string DebugAnimStateName(int state)
    {
        return (MobAnimator.State)state switch
        {
            MobAnimator.State.Idle => "IDLE",
            MobAnimator.State.Walk => "WALK",
            MobAnimator.State.Hurt => "HURT",
            _ => "NONE",
        };
    }

    private static string DebugBehaviourStateName(int state)
    {
        return (MobBehaviourState)state switch
        {
            MobBehaviourState.Idle => "IDLE",
            MobBehaviourState.Wander => "WANDER",
            MobBehaviourState.Chase => "CHASE",
            MobBehaviourState.Hurt => "HURT",
            MobBehaviourState.Dead => "DEAD",
            _ => "NONE",
        };
    }
}

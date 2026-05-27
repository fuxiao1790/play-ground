using Godot;
using PlayGround.Common;
using System.Collections.Generic;
using System;

namespace PlayGround.Player;

public partial class Player : RigidBody2D, IDamageable
{
	[Export] public float player_scale = 4.0f;
	[Export] public int max_attack_count = 4;
	[Export] public int health = 5;
	[Export] public StringName actor_group = null!;
	[Export] public StringName projectile_target_group = null!;
	[Export] public float dash_speed = 560.0f;
	[Export] public float dash_duration = 0.16f;
	[Export] public float dash_cooldown = 0.45f;
	[Export] public bool debug;

	private const int DebugFontSize = 10;
	private static readonly Vector2 DebugOffset = new(-54.0f, -76.0f);
	private const float DebugLineHeight = 11.0f;
	private static readonly Vector2 DebugPadding = new(4.0f, 3.0f);
	private static readonly Vector2 DebugHealthBarSize = new(64.0f, 4.0f);
	private static readonly Color[] DebugColours =
	{
		new(0.55f, 0.85f, 1.00f),
		new(0.40f, 1.00f, 0.55f),
		new(1.00f, 0.85f, 0.25f),
		new(1.00f, 0.35f, 0.35f),
	};

	private AnimatedSprite2D _anim = null!;
	private StateMachineCore _stateMachine = null!;
	private PlayerMovement _movement = null!;
	private PlayerFacing _facing = null!;
	private PlayerStateDriver _stateDriver = null!;
	private PlayerAnimator _animator = null!;
	private Vector2 _aimWorldPosition = Vector2.Zero;
	private int _maxHealth = 1;
	private List<string> _debugWidgetLinesCache = new();
	private Color _debugWidgetColour = Colors.White;
	private float _debugHealthFillRatio = 1.0f;
	private bool _isAlive = true;
	private PlayerAttack _attackLoadout = null!;

	public override void _Ready()
	{
		RequireConfiguredGroup(actor_group, nameof(actor_group));
		RequireConfiguredGroup(projectile_target_group, nameof(projectile_target_group));
		AddConfiguredGroup(actor_group);
		AddConfiguredGroup(projectile_target_group);
		_maxHealth = Mathf.Max(health, 1);

		_anim = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
		_animator = new PlayerAnimator(_anim);

		_stateMachine = new StateMachineCore();
		_stateMachine.StateChanged += OnPlayerStateChanged;
		_stateMachine.Init((int)PlayerAnimator.State.Idle);

		_movement = new PlayerMovement(this);
		_movement.ConfigureDash(dash_speed, dash_duration, dash_cooldown);
		_facing = new PlayerFacing(_anim, this);
		_stateDriver = new PlayerStateDriver(_stateMachine, _movement);
		_attackLoadout = new PlayerAttack(this, max_attack_count);
		_attackLoadout.Setup();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsAlive())
		{
			return;
		}

		_aimWorldPosition = GetGlobalMousePosition();
		_movement.Update(delta, _aimWorldPosition);
		_facing.Update(delta, _aimWorldPosition);
		_attackLoadout.Update(delta, _aimWorldPosition);
		_stateDriver.Update();
	}

	public override void _Process(double delta)
	{
		RefreshDebugDisplay();
	}

	public override void _Draw()
	{
		if (!debug || _debugWidgetLinesCache.Count == 0)
		{
			return;
		}

		DrawDebugWidget();
	}

	public void take_damage(int amount = 1)
	{
		TakeDamage(amount);
	}

	public bool IsAlive() => _isAlive && health > 0;
	public bool is_alive() => IsAlive();
	public int EquippedAttackCount() => _attackLoadout.Count;
	public int equipped_attack_count() => EquippedAttackCount();
	public int PerformEquippedAttacks(Vector2 aimWorldPosition)
	{
		if (!IsAlive())
		{
			return 0;
		}

		return _attackLoadout.TryPerformAll(aimWorldPosition);
	}

	public int perform_equipped_attacks(Vector2 aimWorldPosition) => PerformEquippedAttacks(aimWorldPosition);

	public void TakeDamage(int amount)
	{
		if (amount <= 0 || !IsAlive())
		{
			return;
		}

		health = Mathf.Max(health - amount, 0);
		if (health <= 0)
		{
			SoftDie();
		}
	}

	public void SoftDie()
	{
		if (!_isAlive && health == 0)
		{
			return;
		}

		_isAlive = false;
		health = 0;
		LinearVelocity = Vector2.Zero;
		RemoveConfiguredGroup(projectile_target_group);
		Hide();
		DisableCollisionNode(this);
		DisableCollisionNode(GetNodeOrNull<Area2D>("Hurtbox"));
		SetPhysicsProcess(false);
		SetProcess(false);
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
		if (!debug)
		{
			ClearDebugWidget();
			return;
		}

		_aimWorldPosition = GetGlobalMousePosition();
		RefreshDebugWidgetCache();
		QueueRedraw();
	}

	private void ClearDebugWidget()
	{
		if (_debugWidgetLinesCache.Count > 0)
		{
			_debugWidgetLinesCache = new List<string>();
			QueueRedraw();
		}
	}

	private void RefreshDebugWidgetCache()
	{
		int currentState = _stateMachine.CurrentState;
		string stateName = DebugStateName(currentState);
		_debugWidgetColour = currentState >= 0 && currentState < DebugColours.Length
			? DebugColours[currentState]
			: Colors.White;
		_debugWidgetLinesCache = DebugWidgetLines(stateName);

		int total = Mathf.Max(_maxHealth, health);
		_debugHealthFillRatio = total > 0 ? Mathf.Clamp((float)health / total, 0.0f, 1.0f) : 0.0f;
	}

	private List<string> DebugWidgetLines(string stateName)
	{
		Vector2 inputDir = _movement.input_dir;
		return new List<string>
		{
			$"{stateName} hp:{health}/{_maxHealth}",
			$"dash:{_movement.IsDashing()} cd:{_movement.DashCooldownRemaining():0.00}",
			$"in:({inputDir.X:0.00}, {inputDir.Y:0.00})",
			$"vel:({LinearVelocity.X:0.0}, {LinearVelocity.Y:0.0}) {LinearVelocity.Length():0.0}",
			$"pos:({Position.X:0.0}, {Position.Y:0.0})",
			$"mouse:({_aimWorldPosition.X:0.0}, {_aimWorldPosition.Y:0.0})",
		};
	}

	private void DrawDebugHealthBar(Vector2 origin)
	{
		var barRect = new Rect2(origin, DebugHealthBarSize);
		var fillRect = new Rect2(origin, new Vector2(DebugHealthBarSize.X * _debugHealthFillRatio, DebugHealthBarSize.Y));
		DrawRect(barRect, new Color(0.20f, 0.25f, 0.22f));
		DrawRect(fillRect, new Color(0.20f, 0.90f, 0.30f));
	}

	private void OnPlayerStateChanged(int fromState, int toState)
	{
		int priority = toState == (int)PlayerAnimator.State.Staggered ? 100 : 0;
		_animator.Request((PlayerAnimator.State)toState, priority);
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
				$"Player requires '{propertyName}' to be configured in the scene.");
		}
	}

	private static string DebugStateName(int state)
	{
		return (PlayerAnimator.State)state switch
		{
			PlayerAnimator.State.Idle => "IDLE",
			PlayerAnimator.State.Moving => "MOVING",
			PlayerAnimator.State.Dashing => "DASHING",
			PlayerAnimator.State.Staggered => "STAGGERED",
			_ => "NONE",
		};
	}

}

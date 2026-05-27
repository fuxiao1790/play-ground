using Godot;
using PlayGround.Audio;
using PlayGround.Common;
using System;

public partial class AoeAttack : Node2D, Aoe
{
	private static readonly NodePath AoeCollisionPath = new("CollisionShape2D");

	[Export] public float recovery_seconds = 0.25f;
	[Export] public PackedScene? aoe_effect_scene;
	[Export] public AudioStream? perform_sound;
	[Export] public int aoe_damage = 1;
	[Export] public float aoe_lifetime_seconds = 0.0f;
	[Export] public float aoe_tick_interval_seconds = 0.0f;
	[Export(PropertyHint.Range, "1,4096,1")] public int aoe_count = 1;
	[Export] public bool spawn_at_aim_position;
	[Export] public bool spawn_at_owner_position;
	[Export] public float aoe_burst_radius = 0.0f;
	[Export] public bool randomize_aoe_positions;

	private Node2D _owner = null!;
	private AoeRoot _aoeRoot = null!;
	private readonly RandomNumberGenerator _rng = new();
	private int _aoeTypeId = -1;
	private float _recoveryRemaining;

	public void configure(Node owner)
	{
		_owner = owner as Node2D
			?? throw new InvalidOperationException("AoeAttack requires a Node2D owner.");
		_rng.Randomize();
		BindAoeRoot();
	}

	public void update(double delta, Vector2 aimWorldPosition)
	{
		_recoveryRemaining = Mathf.Max(0.0f, _recoveryRemaining - (float)delta);
	}

	public bool try_perform(Vector2 aimWorldPosition)
	{
		if (aoe_effect_scene == null || _recoveryRemaining > 0.0f)
		{
			return false;
		}

		if (!BindAoeRoot() || _aoeTypeId < 0)
		{
			return false;
		}

		Vector2 center = SpawnCenter(aimWorldPosition);
		DamageSnapshot damage = DamageSnapshot.Single(Mathf.Max(1, aoe_damage));
		int count = Mathf.Max(1, aoe_count);
		float burstRadius = Mathf.Max(0.0f, aoe_burst_radius);
		for (int i = 0; i < count; i++)
		{
			_aoeRoot.SpawnAoe(
				new AoeSpawnRequest(
					_owner,
					_aoeTypeId,
					AoePosition(center, i, count, burstRadius),
					Root.MobHitboxLayer,
					damage,
					aoe_lifetime_seconds,
					aoe_tick_interval_seconds,
					this));
		}

		PlayPerformSound(center);
		_recoveryRemaining = Mathf.Max(0.01f, recovery_seconds);
		return true;
	}

	public void OnHit(in AoeHitContext hit)
	{
		int damage = hit.Damage.TotalWholeAmountOrDefault(Mathf.Max(1, aoe_damage));
		DamageableState.TryApplyDamage(hit.TargetNode, damage);
	}

	private bool BindAoeRoot()
	{
		if (_aoeTypeId >= 0)
		{
			return true;
		}

		Node? currentScene = GetTree().CurrentScene;
		AoeRoot? aoeRoot = currentScene?.GetNodeOrNull<AoeRoot>("AoeManager");
		if (aoeRoot == null || aoe_effect_scene == null)
		{
			return false;
		}

		_aoeRoot = aoeRoot;
		_aoeTypeId = _aoeRoot.RegisterAoeType(aoe_effect_scene, AoeCollisionPath);
		return true;
	}

	private Vector2 SpawnCenter(Vector2 aimWorldPosition)
	{
		if (spawn_at_aim_position)
		{
			return aimWorldPosition;
		}

		return spawn_at_owner_position ? _owner.GlobalPosition : GlobalPosition;
	}

	private Vector2 AoePosition(Vector2 center, int index, int count, float burstRadius)
	{
		if (count <= 1 || burstRadius <= 0.0f)
		{
			return center;
		}

		if (randomize_aoe_positions)
		{
			float randomAngle = _rng.RandfRange(0.0f, Mathf.Tau);
			float randomRadius = Mathf.Sqrt(_rng.Randf()) * burstRadius;
			return center + new Vector2(Mathf.Cos(randomAngle), Mathf.Sin(randomAngle)) * randomRadius;
		}

		float angle = Mathf.Tau * index / count;
		float ring = Mathf.Sqrt((index + 0.5f) / count) * burstRadius;
		return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ring;
	}

	private void PlayPerformSound(Vector2 worldPosition)
	{
		if (perform_sound == null)
		{
			return;
		}

		Node? audioNode = GetNodeOrNull<Node>("/root/AudioManager");
		if (audioNode is AudioManager audioManager)
		{
			audioManager.PlaySound(perform_sound, worldPosition);
		}
	}
}

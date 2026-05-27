using Godot;
using PlayGround.Common;
using System;

namespace PlayGround.Mob;

public sealed class MobProjectileAttack : Projectile
{
	private static readonly NodePath ProjectileCollisionPath = new("CollisionShape2D");

	private readonly Mob _owner;
	private readonly PackedScene _projectileScene;
	private readonly NodePath _projectileSystemPath;
	private readonly float _cooldownSeconds;
	private readonly float _range;
	private readonly float _spawnOffset;
	private readonly float _speed;
	private readonly float _lifetimeSeconds;
	private readonly int _damage;
	private readonly ProjectileTrackingConfig _trackingConfig;
	private Root _projectileRoot = null!;
	private int _projectileTypeId = -1;
	private float _cooldownRemaining;

	public MobProjectileAttack(
		Mob owner,
		PackedScene projectileScene,
		NodePath projectileSystemPath,
		float cooldownSeconds,
		float range,
		float spawnOffset,
		float speed,
		float lifetimeSeconds,
		int damage,
		in ProjectileTrackingConfig trackingConfig)
	{
		_owner = owner;
		_projectileScene = projectileScene;
		_projectileSystemPath = projectileSystemPath;
		_cooldownSeconds = Mathf.Max(0.01f, cooldownSeconds);
		_range = Mathf.Max(1.0f, range);
		_spawnOffset = spawnOffset;
		_speed = Mathf.Max(1.0f, speed);
		_lifetimeSeconds = Mathf.Max(0.05f, lifetimeSeconds);
		_damage = Mathf.Max(1, damage);
		_trackingConfig = trackingConfig;
	}

	public void Setup()
	{
		BindProjectileRootOrThrow();
	}

	public void Update(double delta, Node2D? target)
	{
		_cooldownRemaining = Mathf.Max(0.0f, _cooldownRemaining - (float)delta);
		if (_cooldownRemaining > 0.0f || !DamageableState.IsAlive(target) || target == null)
		{
			return;
		}

		Vector2 aim = target.GlobalPosition - _owner.GlobalPosition;
		float distance = aim.Length();
		if (distance <= 0.001f || distance > _range)
		{
			return;
		}

		Vector2 direction = aim / distance;
		Vector2 spawnPosition = _owner.GlobalPosition + (direction * _spawnOffset);
		_projectileRoot.SpawnProjectile(
			new ProjectileSpawnRequest(
				_owner,
				_projectileTypeId,
				spawnPosition,
				direction * _speed,
				_lifetimeSeconds,
				Root.PlayerHitboxLayer,
				this,
				_trackingConfig,
				DamageSnapshot.Single(_damage)));
		_cooldownRemaining = _cooldownSeconds;
	}

	private void BindProjectileRootOrThrow()
	{
		if (_projectileTypeId >= 0)
		{
			return;
		}

		_projectileRoot = _owner.GetNodeOrNull<Root>(_projectileSystemPath)
			?? throw new InvalidOperationException(
				$"Mob '{_owner.Name}' requires a projectile root at '{_projectileSystemPath}'.");
		_projectileTypeId = _projectileRoot.RegisterProjectileType(_projectileScene, ProjectileCollisionPath);
	}

	public void OnHit(in ProjectileHitContext hit)
	{
		DamageableState.TryApplyDamage(hit.TargetNode, hit.Damage.TotalWholeAmountOrDefault(_damage));
	}
}

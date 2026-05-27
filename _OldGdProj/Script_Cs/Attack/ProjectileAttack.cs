using Godot;
using PlayGround.Audio;
using PlayGround.Common;
using System.Collections.Generic;
using System;

// thid class does not need to be a node 2d.
public partial class ProjectileAttack : Node2D, Projectile, Aoe
{
	private static readonly NodePath ProjectileCollisionPath = new("CollisionShape2D");

	[Export] public float recovery_seconds = 0.05f;
	[Export] public PackedScene? projectile_effect_scene;
	[Export] public AudioStream? perform_sound;
	[Export] public float projectile_speed = 620.0f;
	[Export] public float projectile_lifetime = 1.2f;
	[Export] public float projectile_volley_spread_degrees = 0.0f;
	[Export(PropertyHint.Range, "0,180,0.1")] public float projectile_jitter_degrees = 0.0f;
	[Export(PropertyHint.Range, "1,4096,1")] public int projectile_count = 1;
	[Export] public int projectile_damage = 1;
	[Export(PropertyHint.Range, "0,4096,1")] public int projectile_pierce_count = 0;
	[Export] public bool tracking_enabled;
	[Export] public float tracking_range = 0.0f;
	[Export] public float tracking_turn_speed_degrees = 0.0f;
	[Export] public PackedScene? child_projectile_effect_scene;
	[Export(PropertyHint.Range, "0,4096,1")] public int child_projectile_count = 0;
	[Export(PropertyHint.Range, "0,10,0.01")] public float child_spawn_interval_seconds = 0.0f;
	[Export(PropertyHint.Range, "0,10,0.01")] public float child_spawn_interval_jitter_seconds = 0.0f;
	[Export] public ProjectileChildSpawnPattern? child_spawn_pattern;
	[Export] public float child_damage_multiplier = 1.0f;
	[Export] public bool projectile_direct_damage_enabled = true;
	[Export] public PackedScene? impact_aoe_effect_scene;
	[Export] public int impact_aoe_damage = 1;
	[Export] public float impact_aoe_lifetime_seconds = 0.0f;
	[Export] public float impact_aoe_tick_interval_seconds = 0.0f;

	private Node2D _owner = null!;
	private Root _projectileRoot = null!;
	private AoeRoot? _aoeRoot;
	private int _projectileTypeId = -1;
	private int _childProjectileTypeId = -1;
	private int _impactAoeTypeId = -1;
	private float _recoveryRemaining;
	private readonly List<ProjectileVolleyBuilder.SpawnRequest> _volleyRequests = new();
	private readonly List<ProjectileHitEffect> _hitEffects = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly ProjectileSideSpraySpawnPattern _defaultChildSpawnPattern = new();

	public void configure(Node owner)
	{
		_owner = owner as Node2D
			?? throw new InvalidOperationException("ProjectileAttack requires a Node2D owner.");
		_rng.Randomize();
		ConfigureHitEffects();
		BindProjectileRoot();
		BindAoeRootIfNeeded();
	}

	public void update(double delta, Vector2 aimWorldPosition)
	{
		_recoveryRemaining = Mathf.Max(0.0f, _recoveryRemaining - (float)delta);
	}

	public bool try_perform(Vector2 aimWorldPosition)
	{
		if (projectile_effect_scene == null || _recoveryRemaining > 0.0f)
		{
			return false;
		}

		if (!BindProjectileRoot() || _projectileTypeId < 0)
		{
			return false;
		}

		Vector2 direction = (aimWorldPosition - GlobalPosition).Normalized();
		if (direction == Vector2.Zero)
		{
			return false;
		}

		ProjectileVolleyBuilder.Build(
			_volleyRequests,
			GlobalPosition,
			aimWorldPosition,
			direction,
			projectile_count,
			projectile_volley_spread_degrees,
			projectile_jitter_degrees,
			_rng,
			projectile_speed);
		if (_volleyRequests.Count == 0)
		{
			return false;
		}

		ProjectileTrackingConfig trackingConfig = GetProjectileTrackingConfig();
		DamageSnapshot mainDamage = DamageSnapshot.Single(Mathf.Max(1, projectile_damage));
		int pierceCount = Mathf.Max(0, projectile_pierce_count);
		int childSpawnerId = RegisterChildSpawnDefinition(_volleyRequests.Count, in trackingConfig);
		float childSpawnIntervalSeconds = childSpawnerId > 0 ? Mathf.Max(0.01f, child_spawn_interval_seconds) : 0.0f;

		for (int i = 0; i < _volleyRequests.Count; i++)
		{
			ProjectileVolleyBuilder.SpawnRequest request = _volleyRequests[i];
			_projectileRoot.SpawnProjectile(
				new ProjectileSpawnRequest(
					_owner,
					_projectileTypeId,
					request.Position,
					request.Velocity,
					projectile_lifetime,
					Root.MobHitboxLayer,
					this,
					trackingConfig,
					mainDamage,
					childSpawnerId,
					childSpawnIntervalSeconds,
					Mathf.Max(0.0f, child_spawn_interval_jitter_seconds),
					pierceCount));
		}

		PlayPerformSound(_volleyRequests[0].Position);
		_recoveryRemaining = Mathf.Max(0.01f, recovery_seconds);
		return true;
	}

	private bool BindProjectileRoot()
	{
		if (_projectileTypeId >= 0)
		{
			RegisterChildProjectileTypeIfNeeded();
			RegisterImpactAoeTypeIfNeeded();
			return true;
		}

		Node? currentScene = GetTree().CurrentScene;
		Root? projectileRoot = currentScene?.GetNodeOrNull<Root>("ProjectileManager");
		if (projectileRoot == null || projectile_effect_scene == null)
		{
			return false;
		}

		_projectileRoot = projectileRoot;
		_projectileTypeId = _projectileRoot.RegisterProjectileType(projectile_effect_scene, ProjectileCollisionPath);
		RegisterChildProjectileTypeIfNeeded();
		RegisterImpactAoeTypeIfNeeded();
		return true;
	}

	private void RegisterChildProjectileTypeIfNeeded()
	{
		if (_childProjectileTypeId >= 0 || child_projectile_effect_scene == null)
		{
			return;
		}

		_childProjectileTypeId = _projectileRoot.RegisterProjectileType(child_projectile_effect_scene, ProjectileCollisionPath);
	}

	private int RegisterChildSpawnDefinition(int parentProjectileCount, in ProjectileTrackingConfig trackingConfig)
	{
		if (!ChildSpawningEnabled() || _childProjectileTypeId < 0)
		{
			return 0;
		}

		DamageSnapshot childDamage = DamageSnapshot.Single(
			Mathf.Max(1.0f, projectile_damage * Mathf.Max(0.0f, child_damage_multiplier)));
		return _projectileRoot.RegisterChildSpawnDefinition(
			_owner,
			_childProjectileTypeId,
			child_projectile_count,
			projectile_speed,
			projectile_lifetime,
			Root.MobHitboxLayer,
			this,
			trackingConfig,
			childDamage,
			child_spawn_pattern ?? _defaultChildSpawnPattern,
			parentProjectileCount,
			Mathf.Max(0, projectile_pierce_count));
	}

	private bool ChildSpawningEnabled()
	{
		return child_projectile_effect_scene != null
			&& child_projectile_count > 0
			&& child_spawn_interval_seconds > 0.0f;
	}

	private bool ImpactAoeEnabled()
	{
		return impact_aoe_effect_scene != null;
	}

	private bool BindAoeRootIfNeeded()
	{
		if (!ImpactAoeEnabled())
		{
			return false;
		}

		if (_aoeRoot != null)
		{
			RegisterImpactAoeTypeIfNeeded();
			return _impactAoeTypeId >= 0;
		}

		Node? currentScene = GetTree().CurrentScene;
		_aoeRoot = currentScene?.GetNodeOrNull<AoeRoot>("AoeManager");
		RegisterImpactAoeTypeIfNeeded();
		return _impactAoeTypeId >= 0;
	}

	private void RegisterImpactAoeTypeIfNeeded()
	{
		if (_impactAoeTypeId >= 0 || _aoeRoot == null || impact_aoe_effect_scene == null)
		{
			return;
		}

		_impactAoeTypeId = _aoeRoot.RegisterAoeType(impact_aoe_effect_scene, ProjectileCollisionPath);
	}

	private void SpawnImpactAoe(in ProjectileHitContext hit)
	{
		if (!BindAoeRootIfNeeded() || _aoeRoot == null || _impactAoeTypeId < 0)
		{
			return;
		}

		_aoeRoot.SpawnAoe(
			new AoeSpawnRequest(
				_owner,
				_impactAoeTypeId,
				hit.Position,
				Root.MobHitboxLayer,
				DamageSnapshot.Single(Mathf.Max(1, impact_aoe_damage)),
				impact_aoe_lifetime_seconds,
				impact_aoe_tick_interval_seconds,
				this));
	}

	private void ApplyHitEffects(in ProjectileHitContext hit)
	{
		for (int i = 0; i < _hitEffects.Count; i++)
		{
			_hitEffects[i].Apply(in hit);
		}
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

	private void ConfigureHitEffects()
	{
		_hitEffects.Clear();
		foreach (Node child in GetChildren())
		{
			if (child is ProjectileHitEffect hitEffect)
			{
				hitEffect.Configure(_owner);
				_hitEffects.Add(hitEffect);
			}
		}
	}

	public float GetProjectileVolleySpreadDegrees()
	{
		return projectile_volley_spread_degrees;
	}

	public float GetProjectileJitterDegrees()
	{
		return Mathf.Max(0.0f, projectile_jitter_degrees);
	}

	public int GetProjectileCount()
	{
		return Mathf.Max(1, projectile_count);
	}

	public ProjectileTrackingConfig GetProjectileTrackingConfig()
	{
		return new ProjectileTrackingConfig(
			tracking_enabled,
			tracking_range,
			tracking_turn_speed_degrees);
	}

	public void OnHit(in ProjectileHitContext hit)
	{
		if (ImpactAoeEnabled())
		{
			SpawnImpactAoe(in hit);
		}

		ApplyHitEffects(in hit);

		if (projectile_direct_damage_enabled)
		{
			int damage = hit.Damage.TotalWholeAmountOrDefault(Mathf.Max(1, projectile_damage));
			DamageableState.TryApplyDamage(hit.TargetNode, damage);
		}
	}

	public void OnHit(in AoeHitContext hit)
	{
		int damage = hit.Damage.TotalWholeAmountOrDefault(Mathf.Max(1, impact_aoe_damage));
		DamageableState.TryApplyDamage(hit.TargetNode, damage);
	}
}

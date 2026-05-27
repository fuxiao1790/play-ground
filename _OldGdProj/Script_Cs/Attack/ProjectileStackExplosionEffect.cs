using Godot;
using PlayGround.Common;
using PlayGround.Mob;

public partial class ProjectileStackExplosionEffect : ProjectileHitEffect, Aoe
{
	private static readonly NodePath AoeCollisionPath = new("CollisionShape2D");

	[Export] public MobDebuffStatus stack_debuff_status = MobDebuffStatus.Volatile;
	[Export(PropertyHint.Range, "1,4096,1")] public int stacks_per_projectile_hit = 1;
	[Export(PropertyHint.Range, "1,4096,1")] public int stack_explosion_threshold = 3;
	[Export] public PackedScene? stack_explosion_aoe_effect_scene;
	[Export] public int stack_explosion_damage = 1;
	[Export] public float stack_explosion_lifetime_seconds = 0.0f;
	[Export] public float stack_explosion_tick_interval_seconds = 0.0f;
	[Export] public bool stack_explosion_at_target_position = true;

	private Node2D _owner = null!;
	private AoeRoot? _aoeRoot;
	private int _stackExplosionAoeTypeId = -1;

	public override void Configure(Node2D owner)
	{
		_owner = owner;
		BindAoeRootIfNeeded();
	}

	public override void Apply(in ProjectileHitContext hit)
	{
		if (!Enabled() || hit.TargetNode is not Mob mob)
		{
			return;
		}

		bool thresholdReached = mob.AddDebuffStacks(
			stack_debuff_status,
			Mathf.Max(1, stacks_per_projectile_hit),
			Mathf.Max(1, stack_explosion_threshold));
		if (!thresholdReached)
		{
			return;
		}

		SpawnExplosionAoe(in hit, mob);
	}

	public void OnHit(in AoeHitContext hit)
	{
		int damage = hit.Damage.TotalWholeAmountOrDefault(Mathf.Max(1, stack_explosion_damage));
		DamageableState.TryApplyDamage(hit.TargetNode, damage);
	}

	private bool Enabled()
	{
		return stack_explosion_aoe_effect_scene != null
			&& stacks_per_projectile_hit > 0
			&& stack_explosion_threshold > 0;
	}

	private bool BindAoeRootIfNeeded()
	{
		if (!Enabled())
		{
			return false;
		}

		if (_aoeRoot != null)
		{
			RegisterAoeTypeIfNeeded();
			return _stackExplosionAoeTypeId >= 0;
		}

		Node? currentScene = GetTree().CurrentScene;
		_aoeRoot = currentScene?.GetNodeOrNull<AoeRoot>("AoeManager");
		RegisterAoeTypeIfNeeded();
		return _stackExplosionAoeTypeId >= 0;
	}

	private void RegisterAoeTypeIfNeeded()
	{
		if (_stackExplosionAoeTypeId >= 0 || _aoeRoot == null || stack_explosion_aoe_effect_scene == null)
		{
			return;
		}

		_stackExplosionAoeTypeId = _aoeRoot.RegisterAoeType(stack_explosion_aoe_effect_scene, AoeCollisionPath);
	}

	private void SpawnExplosionAoe(in ProjectileHitContext hit, Mob mob)
	{
		if (!BindAoeRootIfNeeded() || _aoeRoot == null || _stackExplosionAoeTypeId < 0)
		{
			return;
		}

		Vector2 position = stack_explosion_at_target_position ? mob.GlobalPosition : hit.Position;
		_aoeRoot.SpawnAoe(
			new AoeSpawnRequest(
				_owner,
				_stackExplosionAoeTypeId,
				position,
				Root.MobHitboxLayer,
				DamageSnapshot.Single(Mathf.Max(1, stack_explosion_damage)),
				stack_explosion_lifetime_seconds,
				stack_explosion_tick_interval_seconds,
				this));
	}
}

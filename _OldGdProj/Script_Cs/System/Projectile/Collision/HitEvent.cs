using Godot;

/// <summary>
/// Plain world event describing a projectile hit ready for adapter replay.
/// </summary>
public struct HitEvent
{
	public EntityHandle source;
	public EntityHandle target;
	public int projectileTypeId;
	public Vector2 position;
	public DamageSnapshot damage;

	public HitEvent(EntityHandle source, EntityHandle target)
		: this(source, target, 0, Vector2.Zero, DamageSnapshot.Empty)
	{
	}

	public HitEvent(EntityHandle source, EntityHandle target, int projectileTypeId)
		: this(source, target, projectileTypeId, Vector2.Zero, DamageSnapshot.Empty)
	{
	}

	public HitEvent(
		EntityHandle source,
		EntityHandle target,
		int projectileTypeId,
		Vector2 position,
		DamageSnapshot damage)
	{
		this.source = source;
		this.target = target;
		this.projectileTypeId = projectileTypeId;
		this.position = position;
		this.damage = damage;
	}
}

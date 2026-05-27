using Godot;

/// <summary>
/// Gameplay callback payload built by Root when replaying a world hit event.
/// </summary>
public readonly struct ProjectileHitContext
{
	public readonly EntityHandle Source;
	public readonly EntityHandle Target;
	public readonly int ProjectileTypeId;
	public readonly Node2D TargetNode;
	public readonly Vector2 Position;
	public readonly DamageSnapshot Damage;

	public ProjectileHitContext(
		EntityHandle source,
		EntityHandle target,
		int projectileTypeId,
		Node2D targetNode,
		Vector2 position,
		DamageSnapshot? damage = null)
	{
		Source = source;
		Target = target;
		ProjectileTypeId = projectileTypeId;
		TargetNode = targetNode;
		Position = position;
		Damage = damage ?? DamageSnapshot.Empty;
	}
}

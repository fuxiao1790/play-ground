using Godot;
using System.Collections.Generic;

/// <summary>
/// Resource hook for attack-authored child projectile spawn shapes.
/// </summary>
[GlobalClass]
public partial class ProjectileChildSpawnPattern : Resource
{
	public virtual void Build(
		List<ProjectileVolleyBuilder.SpawnRequest> buffer,
		Vector2 parentPosition,
		Vector2 parentVelocity,
		int childCount,
		float childSpeed,
		int tickIndex)
	{
		buffer.Clear();
	}
}

using Godot;

/// <summary>
/// Applies projectile position and lifetime advancement to SoA runtime data.
/// </summary>
public class Movement
{
	// Reads: Velocity
	// Writes: Position, Lifetime, WorldBounds.Position
	public void Apply(ProjectileStore projectiles, int startIndex, int endIndex, double delta)
	{
		for (int i = startIndex; i < endIndex; i++)
		{
			Vector2 step = projectiles.Velocity[i] * (float)delta;
			projectiles.Position[i] += step;
			projectiles.WorldBounds[i] = new Rect2(
				projectiles.WorldBounds[i].Position + step,
				projectiles.WorldBounds[i].Size);
			projectiles.Lifetime[i] -= (float)delta;
		}
	}
}

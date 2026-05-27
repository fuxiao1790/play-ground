using Godot;

/// <summary>
/// Immutable spawn-time configuration for optional projectile target tracking.
/// </summary>
public readonly struct ProjectileTrackingConfig
{
	public static readonly ProjectileTrackingConfig Disabled = new(false, 0.0f, 0.0f);

	public bool Enabled { get; }
	public float Range { get; }
	public float TurnSpeedDegrees { get; }
	public ProjectileTrackingConfig(
		bool enabled,
		float range,
		float turnSpeedDegrees)
	{
		Enabled = enabled;
		Range = Mathf.Max(0.0f, range);
		TurnSpeedDegrees = Mathf.Max(0.0f, turnSpeedDegrees);
	}
}

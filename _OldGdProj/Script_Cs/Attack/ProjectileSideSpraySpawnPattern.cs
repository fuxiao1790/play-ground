using Godot;
using System.Collections.Generic;

/// <summary>
/// Spawns children from the left and right sides of the parent projectile path.
/// </summary>
[GlobalClass]
public partial class ProjectileSideSpraySpawnPattern : ProjectileChildSpawnPattern
{
	[Export(PropertyHint.Range, "0,180,0.1")]
	public float side_spread_degrees = 30.0f;

	public override void Build(
		List<ProjectileVolleyBuilder.SpawnRequest> buffer,
		Vector2 parentPosition,
		Vector2 parentVelocity,
		int childCount,
		float childSpeed,
		int tickIndex)
	{
		buffer.Clear();

		int count = Mathf.Max(1, childCount);
		float speed = Mathf.Max(0.0f, childSpeed);
		Vector2 forward = parentVelocity.Normalized();
		if (forward == Vector2.Zero)
		{
			forward = Vector2.Right;
		}

		Vector2 left = forward.Rotated(-Mathf.Pi * 0.5f);
		Vector2 right = forward.Rotated(Mathf.Pi * 0.5f);
		int leftCount = (count + 1) / 2;
		int rightCount = count / 2;
		int leftIndex = 0;
		int rightIndex = 0;
		float spreadRadians = Mathf.DegToRad(Mathf.Max(0.0f, side_spread_degrees));

		for (int i = 0; i < count; i++)
		{
			if ((i % 2) == 0)
			{
				AddChild(buffer, parentPosition, left, leftIndex, leftCount, spreadRadians, speed);
				leftIndex++;
			}
			else
			{
				AddChild(buffer, parentPosition, right, rightIndex, rightCount, spreadRadians, speed);
				rightIndex++;
			}
		}
	}

	private static void AddChild(
		List<ProjectileVolleyBuilder.SpawnRequest> buffer,
		Vector2 position,
		Vector2 sideDirection,
		int sideIndex,
		int sideCount,
		float spreadRadians,
		float speed)
	{
		float angle = SpreadAngle(spreadRadians, sideIndex, sideCount);
		buffer.Add(new ProjectileVolleyBuilder.SpawnRequest(
			position,
			sideDirection.Rotated(angle) * speed));
	}

	private static float SpreadAngle(float totalSpreadRadians, int shotIndex, int shotCount)
	{
		if (shotCount <= 1)
		{
			return 0.0f;
		}

		float startAngle = -totalSpreadRadians * 0.5f;
		float angleStep = totalSpreadRadians / (shotCount - 1);
		return startAngle + (angleStep * shotIndex);
	}
}
